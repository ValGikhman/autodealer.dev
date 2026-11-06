"""Start MarketCheck's MCP server using AutoDealer.dev's Web.config."""

import argparse
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

    base.MARKETCHECK_API_KEY = key
    # The upstream HTTP client can include provider query strings in diagnostics.
    for name in ("httpx", "httpcore", "services", "mcp"):
        logging.getLogger(name).setLevel(logging.CRITICAL)
    create_mcp_server().run(transport="streamable-http", host="127.0.0.1", port=port, stateless_http=True)


if __name__ == "__main__":
    main()
