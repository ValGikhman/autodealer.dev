from pathlib import Path
import tempfile
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.colors import HexColor, Color, white
from reportlab.platypus import Paragraph
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.utils import ImageReader
from PIL import Image
from pypdf import PdfReader, PdfWriter
from pypdf.generic import NameObject, DictionaryObject, BooleanObject
import pypdfium2 as pdfium

ROOT=Path(tempfile.mkdtemp(prefix='autodealer-brochure-'))
SCRIPT_DIR=Path(__file__).resolve().parent
A=SCRIPT_DIR.parents[1]/'Content'/'images'
OUT=SCRIPT_DIR/'AutoDealer.dev-Trifold-Brochure.pdf'
for name,file in [('UI','segoeui.ttf'),('UI-Bold','segoeuib.ttf'),('UI-Light','segoeuil.ttf')]:
    pdfmetrics.registerFont(TTFont(name,'C:/Windows/Fonts/'+file))
pdfmetrics.registerFontFamily('UI',normal='UI',bold='UI-Bold',italic='UI',boldItalic='UI-Bold')

W,H=792,612
INK=HexColor('#191d22'); DARK=HexColor('#12171c'); SILVER=HexColor('#dce0e4')
RED=HexColor('#bd1026'); MUTED=HexColor('#46535e'); PALE=HexColor('#cbd1d6')
LIGHT=HexColor('#dfe3e7'); PAPER=HexColor('#e9ecef')
LINES=HexColor('#b6bec6')
c=canvas.Canvas(str(ROOT/'brochure-working.pdf'),pagesize=(W,H),pageCompression=1)
c.setTitle('AutoDealer.dev | Dealer Websites Inventory and VIN Intelligence')
c.setAuthor('AutoDealer.dev')
c.setSubject('Six-panel double-sided US Letter tri-fold brochure')
c.setCreator('AutoDealer.dev')

def rect(x,y,w,h,color):
    c.setFillColor(color); c.rect(x,y,w,h,stroke=0,fill=1)
def line(x1,y1,x2,y2,color=LINES,width=.5):
    c.setStrokeColor(color); c.setLineWidth(width); c.line(x1,y1,x2,y2)
def text(x,y,t,size=10,color=INK,font='UI'):
    c.setFillColor(color); c.setFont(font,size); c.drawString(x,y,t)
def para(x,top,w,t,size=10,leading=14,color=INK,font='UI'):
    st=ParagraphStyle('P',fontName=font,fontSize=size,leading=leading,textColor=color)
    p=Paragraph(t,st); _,height=p.wrap(w,600)
    p.drawOn(c,x,top-height)
    assert top-height>=32, (t,top,height)
    return top-height
def headline(x,top,lines,size=25,color=INK,leading=None):
    leading=leading or size*1.13
    for i,t in enumerate(lines):text(x,top-size-i*leading,t,size,color,'UI-Bold')
    return top-size-(len(lines)-1)*leading
def eyebrow(x,y,t,color=RED):
    c.saveState(); c.setFillColor(color)
    ob=c.beginText(x,y); ob.setFont('UI-Bold',7.5); ob.setCharSpace(1.1); ob.textOut(t); c.drawText(ob); c.restoreState()
def logo(x,y,w,dark=False):
    im=Image.open(A/('autodealer-logo.png' if dark else 'autodealer-logo-light.png'))
    c.drawImage(ImageReader(im),x,y,width=w,height=w*im.height/im.width,mask='auto')
def gradient(x,y,w,h,lo,hi):
    # Vector bands create a subtle metallic treatment without raster text.
    for i in range(180):
        t=i/179
        col=Color(lo[0]*(1-t)+hi[0]*t,lo[1]*(1-t)+hi[1]*t,lo[2]*(1-t)+hi[2]*t)
        rect(x,y+h*i/180,w,h/180+.04,col)
def screenshot(name,x,y,w):
    im=Image.open(A/'landing-showcase'/name); h=w*im.height/im.width
    rect(x-3,y-3,w+6,h+6,HexColor('#e3e7ea'))
    c.drawImage(ImageReader(im),x,y,width=w,height=h,mask='auto')
    return h
def feature(x,y,w,title,body):
    rect(x,y-4,3,12,RED)
    text(x+12,y,title,10.4,INK,'UI-Bold')
    return para(x+12,y-10,w-12,body,9.4,13.2,MUTED)
def corner_ticks(folds):
    # Small ticks stay in the blank printer margin and mark actual folds.
    for x in folds:
        line(x,10,x,15,HexColor('#a4abb2'),.35)
        line(x,597,x,602,HexColor('#a4abb2'),.35)

# OUTSIDE: narrower tuck flap | back cover | front cover
# Panel widths 3 5/8, 3 11/16, 3 11/16 inches.
rect(0,0,W,H,PAPER)
rect(18,18,243,576,PALE)
rect(261,18,265.5,576,LIGHT)
gradient(526.5,18,247.5,576,(.055,.075,.09),(.17,.20,.23))
corner_ticks([261,526.5])

# Fold-in flap: a benefit-led overview with the two product paths.
x=40;pw=197
eyebrow(x,558,'CONNECTED BY DESIGN')
headline(x,537,['One platform.','Your next move.'],25)
para(x,464,pw,'Connect your showroom, inventory workflow, and vehicle data. Give your team more room to focus on customers.',11,15,MUTED)
line(x,401,237,401)
eyebrow(x,380,'FOR DEALERSHIPS')
text(x,355,'A complete digital showroom',11,INK,'UI-Bold')
para(x,340,pw,'From the first inventory search to a test-drive request, give shoppers a clear next step. Present your vehicles and manage inventory from one connected workspace.',10,14,MUTED)
eyebrow(x,266,'FOR DEVELOPERS')
text(x,241,'Vehicle data for your application',10.5,INK,'UI-Bold')
para(x,226,pw,'Put VIN decoding to work in your existing application with structured responses and a documented API. Your team need not become walking vehicle encyclopedias.',10,14,MUTED)
line(x,162,237,162)
text(x,141,'Managed service. Practical support.',10.2,INK,'UI-Bold')
para(x,126,pw,'Dealer website plans include hosting, maintenance, and support. You sell cars. A second career in website maintenance is optional.',9.5,13,MUTED)
eyebrow(x,44,'BUILT AROUND YOUR DEALERSHIP',MUTED)

# Back cover: printed call to action and clear contact details.
x=287;pw=214
logo(x,548,210)
eyebrow(x,508,'LET\'S TALK ABOUT YOUR DEALERSHIP')
headline(x,487,['See your next','showroom.'],26)
para(x,411,pw,'Tell us about your inventory, your brand, and the experience you want customers to have. We can walk through the tools that fit your dealership\'s daily work.',10.5,15,MUTED)
eyebrow(x,329,'REQUEST A PERSONAL DEMO')
para(x,309,pw,'See shopper actions, inventory tools, and VIN-powered details in context. Bring your questions. No need to bring the whole car lot.',10.5,15,MUTED)
line(x,241,x+pw,241)
text(x,214,'Start a conversation',12,INK,'UI-Bold')
text(x,183,'autodealer.dev',18,INK,'UI-Bold')
text(x,157,'sales@autodealer.dev',12,RED,'UI-Bold')
para(x,100,pw,'Features and service levels vary by plan. Visit the website for current options and pricing.',7.5,10.5,MUTED)
text(x,37,'© 2026 AutoDealer.dev',7,MUTED)

# Front cover: graphite, chrome branding, a red accent, and real UI imagery.
x=548.5;pw=203.5
logo(x,548,205,True)
rect(x,519,27,3,RED)
eyebrow(x,498,'YOUR DIGITAL SHOWROOM',SILVER)
headline(x,478,['Your next sale','starts online.'],29,white,33)
para(x,393,pw,'Dealer websites. Inventory tools.<br/>Vehicle intelligence. Connected.',10.5,15,SILVER)
screenshot('dealer-inventory-grid.png',x,235,pw)
text(x,221,'REAL PLATFORM. REAL INVENTORY.',7.2,SILVER,'UI-Bold')
line(x,193,x+pw,193,HexColor('#59616a'))
text(x,166,'Show every vehicle at its best.',11,white,'UI-Bold')
para(x,149,pw,'Give shoppers a clear path from inventory browsing to contacting your dealership.',10.2,14,SILVER)
para(x,105,pw,'Let the cars do the talking.<br/>We\'ll handle the website.',9.6,13,white,'UI-Bold')
text(x,61,'autodealer.dev',15,white,'UI-Bold')
text(x,40,'WEBSITES  /  INVENTORY  /  VIN DATA',7.2,SILVER)
c.showPage()

# INSIDE: full-width product story, with reversed panel widths so folds align.
rect(0,0,W,H,PAPER)
rect(18,18,756,576,LIGHT)
rect(18,429,756,165,DARK)
gradient(18,429,756,165,(.055,.075,.09),(.19,.22,.25))
rect(18,427,756,2,RED)
rect(18,18,756,48,DARK)
corner_ticks([265.5,531])
line(265.5,88,265.5,405,LINES,.55)
line(531,88,531,405,LINES,.55)

cols=[(40,203.5),(287.5,221.5),(553,199)]
titles=[['A showroom','worth exploring.'],['Your inventory.','Under control.'],['Every vehicle,','better understood.']]
eyebrows=['DIGITAL SHOWROOM','INVENTORY OPERATIONS','VIN INTELLIGENCE']
descriptions=[
    'Give shoppers an inviting place to find their next vehicle and take the next step.',
    'Bring everyday inventory work into one focused administration workspace.',
    'Turn a 17-character VIN into useful vehicle details for your team and applications.'
]
images=['vehicle-details-gallery.png','inventory-dashboard.png','vin-decoder-report.png']
captions=['Vehicle pages with clear next actions','Inventory visibility at a glance','VIN-powered vehicle information']
for i,(x,pw) in enumerate(cols):
    eyebrow(x,563,eyebrows[i],SILVER)
    headline(x,545,titles[i],23 if i!=2 else 21.7,white,26)
    para(x,478,pw,descriptions[i],10.1,14,SILVER)
    screenshot(images[i],x,302,pw)
    text(x,285,captions[i],7.5,MUTED)

x,pw=cols[0]
feature(x,269,pw,'Make discovery easy','Searchable inventory and useful filters help shoppers find the right fit. Save the treasure hunts for the glove box.')
feature(x,200,pw,'Move interest forward','Pair photo galleries and vehicle details with financing applications, contact options, and test-drive requests.')
feature(x,135,pw,'Make it your dealership','Build familiarity with About, Staff, Testimonials, and Contact pages. Put real people behind the inventory.')

x,pw=cols[1]
feature(x,269,pw,'Less typing. More selling.','Create and edit inventory with VIN-assisted vehicle details. Your keyboard has already worked enough overtime.')
feature(x,200,pw,'Keep inventory moving','Import from Frazer and compatible sources. Preview changes and review additions, updates, and sold vehicles.')
feature(x,135,pw,'See the bigger picture','Manage photos, publishing, inventory history, analytics, and printable QR materials in one workspace.')

x,pw=cols[2]
feature(x,269,pw,'Seventeen characters. Plenty to say.','Decode year, make, model, trim, body style, and drivetrain. Small input. Much better conversation.')
feature(x,200,pw,'Go beyond the basics','Access specifications, equipment, colors, warranties, and fuel economy data to add context to vehicle listings.')
feature(x,135,pw,'Use it your way','Receive JSON or XML for integrations, or HTML for a vehicle report. Your format. Your workflow.')
text(x+12,75,'API access available separately.',7.8,MUTED)

text(40,39,'One connected platform. Built around your dealership.',10.1,SILVER,'UI-Bold')
text(605,39,'Explore more at autodealer.dev',9,SILVER,'UI-Bold')
c.showPage(); c.save()

# Save the two-sided print preference in the PDF itself.
r=PdfReader(ROOT/'brochure-working.pdf');wr=PdfWriter()
wr.clone_document_from_reader(r)
wr.root_object[NameObject('/ViewerPreferences')]=DictionaryObject({
    NameObject('/Duplex'):NameObject('/DuplexFlipShortEdge'),
    NameObject('/PrintScaling'):NameObject('/None'),
    NameObject('/PickTrayByPDFSize'):BooleanObject(True),
})
with OUT.open('wb') as f:wr.write(f)

# Render both sides for full visual QA.
pdf=pdfium.PdfDocument(str(OUT))
for i in range(len(pdf)):
    pdf[i].render(scale=2.5).to_pil().save(ROOT/f'proof-{i+1}.png')
print(OUT)
print('Pages:',len(r.pages),'| US Letter landscape 11 x 8.5 inches')
print('Outside folds: 3.625 and 7.3125 inches from the left')
print('Inside folds: 3.6875 and 7.375 inches from the left')
print('Duplex short edge | Actual size | Quarter-inch printer margin')

print('Rendered proof images:', ROOT)
