"""Create the Russian PDF/Markdown evidence report for the three autumn edits.

Reads final saved-data audit JSONs. Does not modify Unity content or earlier
reports and does not present CPU/color checks as captured gameplay or FPS.
Requires the system Python packages reportlab and pypdf.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
from xml.sax.saxutils import escape

from pypdf import PdfReader
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas
from reportlab.platypus import PageBreak, Paragraph, SimpleDocTemplate, Spacer, Table, TableStyle

REPO = Path(__file__).resolve().parents[2]
REPORTS = REPO / 'Reports/StreetRepair'
BASE = 'c5d2798c790a33b65f3660241cdc443a188f64d2'
TONE = REPORTS / 'asphalt-tone-validation.json'
WARM = REPORTS / 'warm-autumn-validation.json'
UPDATE = REPORTS / 'autumn-update-validation.json'


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def number(value, decimals=0):
    return f'{value:,.{decimals}f}'.replace(',', ' ').replace('.', ',')


def rgb(values):
    return '(' + '; '.join(f'{v:.6g}' for v in values[:3]) + ')'


def setting(value):
    channels = re.findall(r'[rgb]: ([^,}]+)', value)
    return '(' + '; '.join(channels) + ')' if channels else value


def run(output):
    tone = json.loads(TONE.read_text())
    warm = json.loads(WARM.read_text())
    update = json.loads(UPDATE.read_text())
    assert tone['baselineCommit'] == BASE
    assert BASE.startswith(warm['baseCommit'])
    assert tone['targetIntrinsicMeanSRGB8'] == [93, 92, 87]
    assert tone['shaderTextureReadsBefore'] == tone['shaderTextureReadsAfter'] == 3
    assert tone['packedAORoughnessAndAllImporterSettingsByteIdentical']
    assert len(tone['activeAsphaltMaterials']) == 7
    assert all(m['onlyRawVectorGainAdded'] for m in tone['activeAsphaltMaterials'])
    assert warm['savedSceneChecked'] is True
    assert warm['extraRenderPasses'] == warm['extraTextureSamples'] == 0
    assert warm['pavingFoliageAndWhiteConcreteMaterialsUnchanged']

    # Final independent audit provides plaza geometry, immutable scope and budgets.
    facts = report_facts(update)
    for name, file in [('AutumnSans', 'DejaVuSans.ttf'), ('AutumnBold', 'DejaVuSans-Bold.ttf'),
                       ('AutumnMono', 'DejaVuSansMono.ttf')]:
        pdfmetrics.registerFont(TTFont(name, '/usr/share/fonts/truetype/dejavu/' + file))
    styles = getSampleStyleSheet()
    for key in ('Normal', 'BodyText', 'Heading1', 'Heading2', 'Title'):
        style = styles[key]
        style.fontName = 'AutumnSans'
        style.fontSize = {'Title': 18, 'Heading1': 14, 'Heading2': 11}.get(key, 9)
        style.leading = {'Title': 23, 'Heading1': 18, 'Heading2': 15}.get(key, 13)
    styles['Heading2'].fontName = 'AutumnBold'
    styles['Code'].fontName = 'AutumnMono'
    styles['Code'].fontSize = 6.5
    styles['Code'].leading = 9
    flow = []

    def paragraph(text, style='BodyText'):
        flow.extend([Paragraph(escape(str(text)), styles[style]), Spacer(1, 5)])

    def table(rows, widths):
        cells = [[Paragraph(escape(str(value)), styles['BodyText']) for value in row] for row in rows]
        obj = Table(cells, colWidths=widths, hAlign='LEFT', repeatRows=1)
        obj.setStyle(TableStyle([
            ('VALIGN', (0, 0), (-1, -1), 'TOP'),
            ('BACKGROUND', (0, 0), (-1, 0), colors.HexColor('#f0ece4')),
            ('GRID', (0, 0), (-1, -1), .3, colors.HexColor('#bcb8ae')),
            ('TOPPADDING', (0, 0), (-1, -1), 6),
            ('BOTTOMPADDING', (0, 0), (-1, -1), 6),
            ('LEFTPADDING', (0, 0), (-1, -1), 7),
            ('RIGHTPADDING', (0, 0), (-1, -1), 7),
        ]))
        flow.extend([obj, Spacer(1, 10)])

    paragraph('Тон асфальта, широкие площадки и тёплая осень', 'Title')
    paragraph('15 ноября 2026 · hourly992/opd · черновой PR #3')
    paragraph('Ветка: codex/street-paving-leaves-garages. База PR: codex/painted-district. '
              'Исходный коммит трёх изменений: ' + BASE)
    paragraph('Что изменено', 'Heading2')
    paragraph('Откалиброван собственный тон установленного Asphalt020S; северная и западная '
              'площадки расширены с вырезами под дома и защищённую плитку; дневной свет и '
              'существующий финальный проход получили сдержанный тёплый осенний тон. '
              'Автомобили не добавлены.')
    paragraph('Калибровка Asphalt020S', 'Heading2')
    table([
        ['Средний цвет исходного albedo', 'sRGB, 0–255'],
        ['До дополнительного коэффициента', rgb(tone['sourceColorMeanSRGB8'])],
        ['Целевой intrinsic albedo', rgb(tone['targetIntrinsicMeanSRGB8'])],
        ['Расчётный intrinsic albedo после коэффициента', rgb(tone['actualCPUIntrinsicMeanSRGB8'])],
    ], [315, 195])
    paragraph('Проект использует Linear color space. Color JPEG декодируется из sRGB, '
              'затем PBR albedo умножается на сырой Vector _ToneGain = '
              + rgb(tone['rawVectorToneGainLinear']) + '. _Color остаётся белым (1,1,1,1); '
              'SetColor и дополнительное преобразование гаммы к коэффициенту не применяются.')
    paragraph('Число 93/92/87 описывает среднее собственное albedo исходных несжатых '
              'пикселей до освещения и экранной обработки. Это CPU-расчёт цветового '
              'преобразования, а не измерение цвета игрового кадра: итог зависит от '
              'света, BRDF, AO, компрессии, фильтрации и камеры.')
    paragraph('Все семь материалов асфальта сохраняют GUID и прежние свойства; добавлен '
              'только _ToneGain. Четыре исходных JPG ambientCG Asphalt020S, упакованный '
              'AO/roughness PNG и все настройки импорта побайтно совпадают с исходным '
              'коммитом. Источник: https://ambientcg.com/view?id=Asphalt020S, CC0 1.0.')
    paragraph('Шейдер по-прежнему содержит три выборки текстур. Добавлено одно RGB-умножение, '
              'без новой текстуры, выборки, ветвления, насыщенности, шума или runtime-обновления. '
              'Установщик и генератор сохраняют ту же калибровку при повторном применении.')

    flow.append(PageBreak())
    paragraph('Северная и западная асфальтовые площадки', 'Title')
    paragraph(facts['plazaDescription'])
    table(facts['plazaRows'], [180, 165, 165])
    paragraph('Границы и сохранённые объекты', 'Heading2')
    paragraph(facts['preservationDescription'])
    paragraph('Проверки геометрии и коллизии', 'Heading2')
    paragraph(facts['geometryDescription'])
    paragraph('Статический бюджет ресурсов', 'Heading2')
    table(facts['budgetRows'], [270, 120, 120])
    paragraph('Это счётчики сохранённых ресурсов сцены со всеми LOD и статический анализ '
              'шейдеров. Они не измеряют фактические draw calls, VRAM, FPS или время кадра '
              'после импорта Unity. Новый runtime-генератор геометрии не добавлен.')

    flow.append(PageBreak())
    paragraph('Сдержанный тёплый осенний тон', 'Title')
    paragraph('Изменены RGB солнца и трёх составляющих ambient-заполнения. У существующего '
              'StreetPresentation включён warmAutumnGrade = 0,35; для других сцен значение '
              'по умолчанию равно нулю. Положение солнца, его интенсивность, тени, exposure, '
              'bloom, saturation, fog и skybox сохранены.')
    color_names = {'m_AmbientSkyColor': 'Ambient: небо', 'm_AmbientEquatorColor': 'Ambient: экватор',
                   'm_AmbientGroundColor': 'Ambient: земля', 'm_Color': 'Солнце',
                   'warmAutumnGrade': 'Степень тёплого grade'}
    table([['Настройка', 'До', 'После']] + [
        [color_names[item['property']], setting(item['before']), setting(item['after'])] for item in warm['settings']
    ], [150, 180, 180])
    paragraph('Добавленная операция бережно согревает средние тона и тени, сохраняет '
              'расчётную яркость и полностью затухает на ярких белых участках; '
              'воздействие на насыщенные красные, зелёные и осенние оттенки ослаблено. '
              'Исходные цвета материалов и текстур плитки и листьев не менялись.')
    max_hue = max(sample['hueDifferenceDegrees'] for sample in warm['materialSwatches'].values())
    paragraph(f'CPU-проверка добавленной операции: {warm["brightWhiteHighlightSamplesUntinted"]} '
              f'белых образцов без дополнительного оттенка; {warm["sampledDisplayColors"]} '
              f'RGB-образцов, максимальная ошибка яркости '
              f'{warm["maximumAddedGradeLuminanceError"]:.3g}; сдвиг hue проверенных '
              f'цветов плитки/листьев до {max_hue:.3f}°. Это математические проверки '
              'добавленного grade, а не выборка пикселей игрового кадра.')
    paragraph('Новых shader passes, Graphics.Blit, временных RenderTexture, материалов '
              'или texture fetch в экранном шейдере не добавлено. Дополнительная работа '
              'ограничена арифметикой в прежнем финальном проходе.')
    paragraph('Проверки и ограничения', 'Heading2')
    paragraph('Проверены сохранённая сцена, неизменность защищённых ассетов, геометрия '
              'площадок и статические бюджеты. Unity 6000.6.7f1 отсутствует: импорт, '
              'компиляция C#/шейдеров, сборка, запуск и замеры FPS/GPU-времени '
              'не выполнялись. Отсутствие регрессии времени кадра ещё не измерено.')
    paragraph('Игровые скриншоты после сборки пользователь снимает самостоятельно. '
              'Этот отчёт не содержит игровых кадров и не заменяет реальные парные '
              'кадры и проверку одинаковых маршрутов на одной машине.')
    for path in (TONE, WARM, UPDATE):
        paragraph(path.name + ' · SHA-256 ' + sha256(path), 'Code')
    paragraph('Воспроизведение PDF: python Maintenance/StreetRepair/report_autumn_tone_plazas.py')

    def footer(pdf_canvas, doc):
        pdf_canvas.setFont('AutumnSans', 7)
        pdf_canvas.setFillColor(colors.HexColor('#645b4d'))
        pdf_canvas.drawString(42, 22, 'Тон, площадки, осень · PR #3 · 15.11.2026 · сохранённые данные')
        pdf_canvas.drawRightString(A4[0] - 42, 22, str(doc.page))

    def deterministic_canvas(*args, **kwargs):
        kwargs['invariant'] = 1
        return canvas.Canvas(*args, **kwargs)

    output.parent.mkdir(parents=True, exist_ok=True)
    SimpleDocTemplate(str(output), pagesize=A4, rightMargin=42, leftMargin=42,
                      topMargin=35, bottomMargin=38, title='Тон асфальта, площадки и осень — PR #3',
                      author='Codex').build(flow, onFirstPage=footer, onLaterPages=footer,
                                           canvasmaker=deterministic_canvas)
    document = PdfReader(str(output))
    text = '\n'.join(page.extract_text() for page in document.pages)
    assert len(document.pages) == 3, 'Layout changed: inspect every PDF page before delivery'
    for expected in (BASE, '93', '92', '87', 'CPU', 'не выполнялись'):
        assert expected in text, ('Missing report content', expected)
    markdown = [
        '# Тон асфальта, широкие площадки и тёплая осень', '',
        '15 ноября 2026 · `codex/street-paving-leaves-garages` · черновой PR #3.', '',
        'Исходный коммит: `' + BASE + '`. База PR: `codex/painted-district`.', '',
        '## Три изменения', '',
        '1. Asphalt020S: средний intrinsic albedo откалиброван к sRGB(93,92,87) '
        'через сырой Vector `_ToneGain`; `_Color` остаётся белым. Это среднее '
        'albedo до освещения и камеры, вычисленное на CPU, а не цвет игрового кадра. '
        'Четыре JPG, packed AO/roughness и все importers побайтно сохранены; '
        'семь материалов сохраняют GUID и остальные свойства.',
        '2. ' + facts['plazaDescription'],
        '3. Изменены RGB солнца/ambient и `warmAutumnGrade=0.35` в прежнем финальном '
        'проходе. Exposure, intensity, pose, shadows, fog, skybox, bloom и saturation '
        'сохранены. Исходные цвета плитки и листьев не менялись. Автомобили не добавлены.', '',
        '## Геометрия и сохранность', '',
        facts['preservationDescription'], '', facts['geometryDescription'], '',
        '| ' + ' | '.join(facts['plazaRows'][0]) + ' |',
        '| --- | --- | --- |',
    ]
    markdown.extend('| ' + ' | '.join(map(str, row)) + ' |' for row in facts['plazaRows'][1:])
    markdown.extend(['', '## Статические счётчики', '',
                     '| ' + ' | '.join(facts['budgetRows'][0]) + ' |', '| --- | --- | --- |'])
    markdown.extend('| ' + ' | '.join(map(str, row)) + ' |' for row in facts['budgetRows'][1:])
    markdown.extend([
        '', 'Asphalt: 3 → 3 texture reads, добавлено одно RGB-умножение. '
        'Warm grade: +0 shader passes, +0 texture reads, без дополнительных Blit/RT. '
        'Это статические счётчики, не фактические draw calls, VRAM или FPS.', '',
        f'CPU-проверка grade: {warm["brightWhiteHighlightSamplesUntinted"]} белых образцов '
        f'без дополнительного оттенка, {warm["sampledDisplayColors"]} RGB-образцов, '
        f'ошибка яркости до {warm["maximumAddedGradeLuminanceError"]:.3g}, '
        f'сдвиг hue образцов плитки/листьев до {max_hue:.3f}°.', '',
        '## Проверки и ограничения', '',
        '- [Калибровка albedo](asphalt-tone-validation.json).',
        '- [Сохранённые свет/grade и математические инварианты](warm-autumn-validation.json).',
        '- [Независимая проверка трёх изменений](autumn-update-validation.json).', '',
        'Unity 6000.6.7f1 отсутствует. Импорт, компиляция, сборка, запуск, '
        'реальные парные игровые кадры и измерения FPS/GPU-времени не выполнялись. '
        'Игровые скриншоты после сборки пользователь снимает самостоятельно. '
        'Проверка отсутствия регрессии требует игры на одной машине и одинаковых маршрутах.', '',
        'PDF и этот Markdown воспроизводятся командой:', '',
        '```sh', 'python Maintenance/StreetRepair/report_autumn_tone_plazas.py', '```', '',
    ])
    output.with_suffix('.md').write_text('\n'.join(markdown))
    print(json.dumps({'output': str(output), 'pages': len(document.pages), 'textVerified': True}, ensure_ascii=False))


def report_facts(update):
    """Translate the final independent geometry audit into printable evidence."""
    assert update['baselineCommit'] == BASE
    assert update['sceneChangesLimitedToLightingGradeGroundMeshRefsAndRetiredEmptyCourt']
    assert update['unchangedPaverSubmeshes'] == 21
    assert update['cars']['savedVehicleComponents'] == 0
    assert update['vegetationExclusion']['survivingGrassLawnCloverInsidePlazas'] == 0
    geometry = json.loads((REPORTS / 'autumn-plaza-geometry.json').read_text())
    budget = update['activeDirectSavedRenderersAllLodsUpperBound']
    rows = [['Площадка', 'Северная', 'Западная']]
    rows.append(['Площадь асфальта без рамп, м²'] + [
        number(update['plazaCoverageAndExistingPavingHouseExclusion'][k]['expectedAreaSquareMeters'], 2)
        for k in ('north', 'west')])
    rows.append(['Наружный припуск вдоль стен, м', 'около 3,54', 'около 4,03'])
    rows.append(['Треугольники новой площадки'] + [
        str(geometry['plazas'][k]['generatedTriangles']) for k in ('north', 'west')])
    budget_rows = [['Прямые активные объекты, все LOD', 'До', 'После']]
    for label, key in [('Треугольники', 'triangles'), ('Вершины', 'vertices'),
                       ('Непустые renderer', 'activeRenderers'), ('Непустые подмеши', 'nonemptySubmeshes')]:
        budget_rows.append([label, number(budget['before'][key]), number(budget['after'][key])])
    collision = update['collision']
    links = update['connectivity']
    return {
        'plazaDescription': 'Обе пары окружены единым асфальтом вдоль наружных стенок, между '
        'навесами и за ними, с выходом к существующим проездам и парковкам. Северная '
        'площадка занимает x=16,1…36,3; z=91,8…137,5, западная — x=−121…−81; '
        'z=60,5…81,7. Подъём двора 0,85 м за 6 м, ровная верхняя часть и задний сход '
        'за 5 м сохранены. Снаружи последние 0,85 м сопрягаются с прежним грунтом.',
        'plazaRows': rows,
        'preservationDescription': 'Площадки точно обрезаны по домам и существующей плитке; '
        'газонных прослоек внутри покрытия нет. У северных задних углов фасады и плитка '
        'ограничивают свободный проход до 0,7–1 м. Все 9 200 блоков трансформов/prefab, '
        '21 плиточный подмеш, исходные модели въездов, бетон и профиль дороги сохранены. '
        'Шесть общих грунтовых ресурсов изолированы для основной сцены. Новые renderer '
        'не добавлены; пустые прежние renderer/коллайдер двора отключены, объекты сохранены.',
        'geometryDescription': f'Проверены {collision["clearDescendingRampProbes"]} проб свободного '
        f'спуска, {collision["courtyardCentreSideStripsAndRearApproachProbes"]} проб покрытия/коллизии '
        f'двора и {links["continuousConnectorCollisionProbes"]} проб соединений. Все четыре устья '
        'и семь дорожных/парковочных ориентиров входят в один компонент фактического асфальта '
        '(допуск шва 12 мм). Проверка травы и клевера внутри площадок не нашла остатков. '
        'Совпадение покрытия с независимой маской — с ошибкой площади менее 0,001 м².',
        'budgetRows': budget_rows,
    }


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=REPORTS / 'autumn-tone-plazas-report.pdf')
    run(parser.parse_args().output)
