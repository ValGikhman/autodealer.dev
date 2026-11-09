"""Start MarketCheck's MCP server using AutoDealer.dev's Web.config."""

import argparse
from datetime import datetime, timezone
import logging
from pathlib import Path
import sys
from urllib.parse import urlsplit
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("server_directory", type=Path, help="Official MarketCheck MCP checkout")
    parser.add_argument("--web-config", type=Path, default=Path(__file__).resolve().parents[1] / "Web.config")
    args = parser.parse_args()

    try:
        settings = {
            item.get("key"): item.get("value", "").strip()
            for item in ET.parse(args.web_config).findall("./appSettings/add")
        }
        key = settings.get("MarketCheck:ApiKey")
        endpoint = urlsplit(settings.get("MarketCheck:McpUrl", ""))
        port = endpoint.port
    except (OSError, ET.ParseError, ValueError):
        parser.error("Read MarketCheck:ApiKey and MarketCheck:McpUrl from a valid Web.config.")

    if not key:
        parser.error("Set MarketCheck:ApiKey in AutoDealer.dev Web.config.")
    if (endpoint.scheme != "http" or endpoint.hostname not in ("127.0.0.1", "localhost")
            or not port or endpoint.path != "/mcp" or endpoint.username is not None
            or endpoint.query or endpoint.fragment):
        parser.error("Set MarketCheck:McpUrl to a loopback URL such as http://127.0.0.1:8765/mcp.")

    checkout = args.server_directory.resolve()
    if not (checkout / "server.py").is_file():
        parser.error("Point server_directory to the official MarketCheck MCP checkout.")
    sys.path.insert(0, str(checkout))

    from services import base
    from server import create_mcp_server
    from starlette.responses import Response

    base.MARKETCHECK_API_KEY = key
    quota_headers = {}
    quota_names = ("quota-remaining", "quota-limit", "quota-reset-time")
    shared_client = base.get_shared_client

    async def capture_quota(response):
        if response.request.url.host != "api.marketcheck.com":
            return
        headers = {name: response.headers[name] for name in quota_names if name in response.headers}
        if "quota-remaining" in headers or "quota-limit" in headers:
            quota_headers.clear()
            quota_headers.update(headers, **{"quota-observed-at": datetime.now(timezone.utc).isoformat()})

    def get_shared_client():
        client = shared_client()
        hooks = client.event_hooks["response"]
        if capture_quota not in hooks:
            hooks.append(capture_quota)
        return client

    base.get_shared_client = get_shared_client
    mcp = create_mcp_server()

    @mcp.custom_route("/marketcheck-quota", methods=["GET"])
    async def marketcheck_quota(request):
        return Response(headers={**quota_headers, "Cache-Control": "no-store"})

    # The upstream HTTP client can include provider query strings in diagnostics.
    for name in ("httpx", "httpcore", "services", "mcp"):
        logging.getLogger(name).setLevel(logging.CRITICAL)
    mcp.run(transport="streamable-http", host="127.0.0.1", port=port, stateless_http=True)


if __name__ == "__main__":
    main()
