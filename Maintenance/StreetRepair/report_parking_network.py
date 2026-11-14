"""Generate the separate technical PDF required for this PR update.

This report contains saved-data evidence, not fabricated gameplay frames/FPS.
Run with the system Python (reportlab installed in the cloud environment).
"""
import json
from pathlib import Path
from xml.sax.saxutils import escape
from reportlab.lib import colors
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle

REPO=Path(__file__).resolve().parents[2]
OUT=REPO/'Reports/StreetRepair'
BASE='2fb3b792ddba4966e3aceb8c74866f81827a36bc'

def run():
 qa=json.loads((OUT/'parking-network-validation.json').read_text())
 general=json.loads((OUT/'validation.json').read_text())
 pdfmetrics.registerFont(TTFont('DejaVu','/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'))
 pdfmetrics.registerFont(TTFont('DejaVuBold','/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'))
 styles=getSampleStyleSheet()
 for key in ['Normal','BodyText','Heading1','Heading2','Title']:
  styles[key].fontName='DejaVu'
  styles[key].fontSize=10 if key not in ['Title','Heading1','Heading2'] else {'Title':18,'Heading1':14,'Heading2':12}[key]
  styles[key].leading=15 if key not in ['Title','Heading1','Heading2'] else 22
 styles['Heading2'].fontName='DejaVuBold'
 flow=[]
 def p(text,style='BodyText'):
  flow.append(Paragraph(escape(text),styles[style]));flow.append(Spacer(1,5))
 p('Связанные проезды и четыре въезда в подземную парковку','Title')
 p('14 ноября 2026 · hourly992/opd · черновой PR #3')
 p('Рабочая ветка: codex/street-paving-leaves-garages. База PR: codex/painted-district. Исходный коммит этого обновления: '+BASE)
 p('Изменения в сохранённой сцене','Heading2')
 p('У северных въездов №5/7 добавлены недостающие асфальтовые полосы шириной 4,4 м от устьев к существующему кольцевому проезду. Кольцо уже связано с основной улицей, парковками и соседними дворами. Новые участки замыкаются на сохранённые покрытия, без обрывов посреди газона.')
 p('По западную сторону основной улицы добавлена такая же пара: устья (-108,67) и (-108,75.2), поворот Y=90°. Подъезд обходит старую диагональную полосу, вырезанную из коллизии рамп. Между въездами асфальт поднимается к ровному двору. Положение оценочное: свободный коридор проверен по фактическим зданиям и объектам; буквальное отражение прежних координат попало бы в лес.')
 p('Навес — единая связная прозрачная оболочка, её края опираются на белые стенки. Непрозрачные дуги и висящие боковые рамки убраны. Геометрия бетонных стен, ворота, профиль спусков и белый материал сохранены. Четыре въезда пакетированы в четыре прежних renderer; новые объекты содержат отдельные коллайдеры и метаданные, без runtime-генерации.')
 p('Источники: фотографии 14–17, панорамы 20–24 и вид сверху 25 из задания; сохранённые оси улицы и дворовых проездов. Плитка, листья, свет, дома и профиль проезжей части в этом обновлении не изменены. Общие исторические ассеты травы сохранены; новые вырезы изолированы для основной сцены.')
 p('Проверки и бюджет ресурсов','Heading2')
 p(f'Общий валидатор проверил {general["validatedMeshAssets"]} ассетов. Геометрия проезжей части ({general["carriagewayTrianglesUnchanged"]} треугольников) и переходов ({general["crosswalkTrianglesUnchanged"]}) совпадает с исходной. Независимый отчёт: parking-network-validation.json.')
 p('Независимые проверки: четыре отдельные коллизии и позиции, отсутствие плоского грунта над спусками, связность асфальта к основной дороге и дворам, непрерывность соединений, плоскость двора, касательные/нормали и цельность навеса; неизменность плитки, бетона, листьев, света и существующих объектов.')
 totals=qa['activeDirectSavedRenderersAllLodsUpperBound']
 rows=[['Сохранённые ресурсы со всеми LOD','До','После']]
 for label,key in [('Треугольники','triangles'),('Непустые подмеши','nonemptySubmeshes')]:
  rows.append([label,f'{totals["before"][key]:,}'.replace(',',' '),f'{totals["after"][key]:,}'.replace(',',' ')])
 table=Table(rows,colWidths=[280,90,90],hAlign='LEFT')
 table.setStyle(TableStyle([('FONTNAME',(0,0),(-1,-1),'DejaVu'),('FONTSIZE',(0,0),(-1,-1),9),('BACKGROUND',(0,0),(-1,0),colors.HexColor('#e9edf2')),('GRID',(0,0),(-1,-1),.3,colors.HexColor('#aab2bd')),('TOPPADDING',(0,0),(-1,-1),7),('BOTTOMPADDING',(0,0),(-1,-1),7)]))
 flow.extend([table,Spacer(1,10)])
 p('Это верхняя оценка геометрии и частей ресурсов, а не FPS, GPU-время или фактические draw calls. Уменьшение геометрии не доказывает отсутствие регрессии времени кадра без запуска на одной машине и одном маршруте.')
 p('Незавершённая установка Asphalt020S','Heading2')
 p('Оригинальные карты ambientCG Asphalt020S ещё не загружены и материалы асфальта не заменены. Сетевой proxy среды запретил официальную загрузку (CONNECT 403). Нужны разрешения для ambientcg.com, acg-download.struffelproductions.com и docs.ambientcg.com. Другие текстуры не подбирались.')
 p('Подготовлен install_asphalt020s.py: проверка четырёх оригинальных карт Color/NormalGL/Roughness/AO из официального Asphalt020S_2K-JPG.zip до изменения материалов; сохранение оригиналов, CC0 1.0, источника и SHA; упаковка AO/roughness для трёх runtime-сэмплеров. Источник: https://ambientcg.com/view?id=Asphalt020S. Масштаб материала 1,85 × 1,85 м. Синтетический тест упаковки не заменяет проверку настоящих исходных карт.')
 p('Ограничения','Heading2')
 p('Unity 6000.6.7f1 отсутствует в среде. Импорт шейдеров, компиляция C#, Windows/Linux-сборки, запуск и замеры FPS не выполнялись. Временное техническое Blender-превью проверяет только геометрию с условными материалами. Игровые скриншоты после сборки пользователь снимает самостоятельно; за игровые кадры этот отчёт схемы/превью не выдаёт.')
 p('PR остаётся черновиком. Слияние в main и merge PR не выполнялись.')
 path=OUT/'ParkingNetwork-2026-11-14.pdf'
 SimpleDocTemplate(str(path),pagesize=A4,rightMargin=35,leftMargin=35,topMargin=35,bottomMargin=35,title='Связанные дворовые проезды и въезды — PR #3',author='Codex').build(flow)
 print(path)

if __name__=='__main__':run()
