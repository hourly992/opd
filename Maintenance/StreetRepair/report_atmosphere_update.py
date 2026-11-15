"""Separate saved-data report; never substitutes CPU tests for game captures."""
import hashlib
import json
from pathlib import Path
from xml.sax.saxutils import escape

from pypdf import PdfReader
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas
from reportlab.platypus import Paragraph, Spacer, Table, TableStyle, SimpleDocTemplate, PageBreak

REPO = Path(__file__).resolve().parents[2]
REPORTS = REPO / 'Reports/StreetRepair'
BASE = '0d73c23ee7bd71c144bf188d0f5b4cb91304c979'
FEATURES = [
    ('1. Свет', 'Солнце 22° над горизонтом, золотистый прямой свет; soft shadows, strength 0,72, мягкое свечение светлых мест.'),
    ('2. Частицы', 'Редкие пылинки только на освещённых участках; падающие листья начинаются на реальных треугольниках крон.'),
    ('3. Лучи', 'Прежние depth-masked shafts смягчены; здания и кроны закрывают источник лучей. Без дополнительного полного прохода.'),
    ('4. Дымка и края', 'Лёгкий Exp2 fog вдали, density 0,0015; виньетка 0,09 в существующем финальном проходе.'),
    ('5. Обзор', 'Вертикальный FOV 60° → 64°, положение камеры и архитектура сохранены.'),
    ('6. Ходьба', 'Плавное детерминированное покачивание по фактическому перемещению: 9 мм, без случайной тряски.'),
    ('7. Shift', 'Бег 6,2 м/с против ходьбы 4 м/с; покачивание сильнее в 1,3 раза, без пульсаций FOV.'),
    ('8. Небо', 'Голубой верх, золотистый горизонт; два слоя мягких объёмно затенённых облаков, медленный дрейф, диск солнца и ореол.'),
    ('9. Облачные тени', 'Общая карта того же облачного поля ослабляет прямой солнечный свет внутри прежних GI-проходов материалов.'),
    ('10. Окна', 'Окна отражают то же движущееся небо из мировой позиции стекла; прежний неподвижный probe не перекрывает облака.'),
    ('11. Ветер', 'Движение деревьев, кустов и травы замедлено; корни неподвижны, амплитуда уменьшена до 82% прежней.'),
    ('12. Асфальт', 'Компенсация уже освещённого результата и активного camera grade. Исходный Asphalt020S и свойства PBR сохранены.'),
    ('13. Тёплая осень', 'Warm grade 0,35 → 0,95 и более выраженное золотистое тонирование; защита яркого белого и насыщенных цветов.'),
]


def run():
    names = ['atmosphere-update-validation.json', 'atmosphere-scene-settings.json',
             'asphalt-display-validation.json', 'weather-validation.json',
             'particle-wind-validation.json', 'camera-walk-validation.json']
    data = {n: json.loads((REPORTS/n).read_text()) for n in names}
    assert data[names[0]]['baselineCommit'] == BASE
    asphalt = data['asphalt-display-validation.json']
    assert asphalt['maximumFinalSRGB8ChannelSpread'] < 1
    pdfmetrics.registerFont(TTFont('Atmosphere', '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'))
    styles = getSampleStyleSheet()
    for name in ('Title', 'Heading2', 'BodyText', 'Code'):
        styles[name].fontName = 'Atmosphere'
        styles[name].fontSize = 17 if name == 'Title' else 10 if name == 'Heading2' else 8.7
        styles[name].leading = 21 if name == 'Title' else 12
    styles['Code'].fontSize = 6.3
    styles['Code'].leading = 9
    flow = []

    def p(text, style='BodyText'):
        flow.extend([Paragraph(escape(str(text)), styles[style]), Spacer(1, 6)])

    def table(rows, widths):
        cells = [[Paragraph(escape(str(c)), styles['BodyText']) for c in row] for row in rows]
        t = Table(cells, colWidths=widths, repeatRows=1)
        t.setStyle(TableStyle([('VALIGN',(0,0),(-1,-1),'TOP'),
                              ('BACKGROUND',(0,0),(-1,0),colors.HexColor('#eee8dd')),
                              ('GRID',(0,0),(-1,-1),.3,colors.lightgrey),
                              ('TOPPADDING',(0,0),(-1,-1),5),('BOTTOMPADDING',(0,0),(-1,-1),5)]))
        flow.extend([t,Spacer(1,10)])

    p('Осенняя атмосфера, небо и прогулка', 'Title')
    p('15 ноября 2026 · hourly992/opd · черновой PR #3 · codex/street-paving-leaves-garages')
    p('Исходный коммит обновления: '+BASE+'. База PR: codex/painted-district.')
    table([('Требование', 'Сохранённая реализация')] + FEATURES, [112,398])
    flow.append(PageBreak())
    p('Согласованное небо и бюджет', 'Title')
    p('Солнце на небе, освещение, пылинки, облачные тени и отражения используют одно направление света и общее облачное поле. '
      'Для ambient формируются SH-коэффициенты неба; для отражений — небольшая обновляемая cubemap. Окна напрямую используют полную модель неба.')
    p('Облака имеют два слоя и освещение толщины/края; дорогое объёмное трассирование не добавлено. '
      'Дрейф общей карты теней задаётся непрерывным переносом мировой проекции. Перезапись её опорной области выполняется порциями '
      'раз в минуту; перемещение не ждёт обновления текстуры. Мягкость ограничена пространственным разрешением 128².')
    table([
        ['Ресурс / работа', 'Ограничение или изменение'],
        ['Сохранённая геометрия, дома, проезды, плитка', 'Побайтно сохранены; новые renderer в сцене не создаются'],
        ['Солнечная shadow map', '4096 → 2048; soft shadows; без новых источников света'],
        ['Контактные тени / лучи', 'AO downsample 3 → 4; radial 24 → 12 samples; masks RHalf в четверть размера'],
        ['Полные экранные проходы', 'Новые не добавлены; виньетка и grade в существующем final pass'],
        ['Облака и общая карта теней', 'Две 128² RGBA32 текстуры; один общий shadow fetch в освещении основного directional sun'],
        ['Отражения и ambient', 'Cubemap 16², одна грань каждые 0,5 с; SH каждые 2 с; без scene-capture camera'],
        ['Пылинки', 'Defaults 24, пул 32 quad / 64 tri; до одного DrawMesh и двух sun-raycast за кадр'],
        ['Падающие листья', '48 сохранённых траекторий; 106 отключены; рядом максимум 3 падающих + 1 наземный'],
        ['Асфальт', '3 карты материала +1 общая тень облаков; 32 uniform gains, без LUT-текстуры'],
    ], [246,264])
    p('Native Light.cookie не назначается: в Built-in Forward он может вызвать дополнительную перерисовку объектов. '
      'Облачная тень умножает только прямой gi.light.color в прежнем ForwardBase; ambient и исходные цвета материалов сохранены.')
    p('Данные выше — статические ограничения и счётчики, не измеренные FPS, draw calls, GPU-время или VRAM. '
      'Дополнительные вычисления и выборки существуют; фактический итог на целевой машине ещё не измерен.')
    flow.append(PageBreak())
    p('Проверки и границы результата', 'Title')
    p('Независимый аудит проверяет сохранность всех существующих объектов, геометрии, prefab, текстур и исходных цветов материалов; '
      'разрешены только объявленные изменения света, камеры, двух компонентов и метаданных траекторий. В сцене нет автомобилей, '
      '19 массивов парковок остаются пустыми, runtime-спавн автомобилей отсутствует.')
    p('Асфальт проверен после уравнений освещения и активного Painted/Warm display transform: '
      f'{asphalt["float32BrightnessSweepSamples"]} яркостей и {asphalt["sunAmbientCookieAOCombinations"]} сочетаний света, облачной тени и AO. '
      f'Максимальный разброс каналов итогового sRGB — {asphalt["maximumFinalSRGB8ChannelSpread"]:.3f}/255. '
      'Это float32 CPU-модель, а не измерение игрового кадра. Поздние bloom, shafts и дальняя дымка могут локально влиять на цвет.')
    p('Проверки ходьбы охватывают 30/60/120/144 Гц, возврат камеры после остановки, упор в стену, паузу и потерю фокуса. '
      'Привязка падающих листьев проверена по настоящим треугольникам крон, грунту, домам, стволам и сохранённым препятствиям. '
      'Проекции облачного поля и обновление ресурсов проверены численно и по исходникам.')
    p('Unity 6000.6.7f1 в среде отсутствует. Импорт/компиляция C# и шейдеров, сборка Windows/Linux, запуск игры, '
      'визуальная оценка финального неба/цвета/комфорта камеры и измерение FPS не выполнялись. '
      'После сборки необходимо проверить одинаковые маршруты на одной машине. Игровые скриншоты снимает пользователь; '
      'этот PDF не содержит игровых кадров и не выдаёт схемы или CPU-расчёты за них.')
    p('Воспроизведение и доказательства', 'Heading2')
    for name in names:
        digest = hashlib.sha256((REPORTS/name).read_bytes()).hexdigest()
        p(name+'\nSHA-256 '+digest, 'Code')
    p('PDF: python Maintenance/StreetRepair/report_atmosphere_update.py. '
      'Проверка: python Maintenance/StreetRepair/validate_atmosphere_update.py. '
      'Одноразовая запись сцены из исходного коммита: atmosphere_scene.py; повторный запуск на готовой сцене запрещён guard-проверкой.')

    def footer(c,doc):
        c.setFont('Atmosphere',7)
        c.drawString(42,22,'Осенняя атмосфера · PR #3 · сохранённые данные · 15.11.2026')
        c.drawRightString(A4[0]-42,22,str(doc.page))

    def invariant(*args,**kwargs):
        kwargs['invariant']=1
        return canvas.Canvas(*args,**kwargs)
    output=REPORTS/'Atmosphere-2026-11-15.pdf'
    SimpleDocTemplate(str(output),pagesize=A4,leftMargin=42,rightMargin=42,topMargin=35,bottomMargin=38,
                      title='Осенняя атмосфера — PR #3',author='Codex').build(flow,onFirstPage=footer,onLaterPages=footer,canvasmaker=invariant)
    pdf=PdfReader(str(output))
    text='\n'.join(page.extract_text() for page in pdf.pages)
    assert len(pdf.pages)==3, 'Inspect changed report layout'
    assert BASE in text and 'не выполнялись' in text
    (REPORTS/'atmosphere-update.md').write_text('# Осенняя атмосфера\n\nИсходный коммит: `'+BASE+'`.\n\n'+
        '\n\n'.join('**'+a+'** — '+b for a,b in FEATURES)+'\n\n'+
        '[PDF](Atmosphere-2026-11-15.pdf) · [Проверка](atmosphere-update-validation.json).\n\n'+
        'Геометрия и исходные цвета материалов сохранены, автомобилей нет. Unity, сборка, игровые кадры и FPS не проверялись. '+
        'Статические лимиты не гарантируют отсутствие регрессии времени кадра; нужен замер на одной машине.\n')
    print(json.dumps({'pdf':str(output),'pages':len(pdf.pages),'textVerified':True}))


if __name__ == '__main__':
    run()
