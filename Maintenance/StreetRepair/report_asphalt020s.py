"""Rebuild the separate Asphalt020S evidence PDF from the installed manifests.

Run with system Python and reportlab+pypdf. Does not change Unity assets,
regenerate a scene, contact a service, or rewrite historical PDFs.
"""
import argparse
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
from reportlab.platypus import (
    PageBreak, Paragraph, SimpleDocTemplate, Spacer, Table, TableStyle,
)

REPO = Path(__file__).resolve().parents[2]
REPORTS = REPO / 'Reports/StreetRepair'
SOURCE_FOLDER = REPO / 'Unity_Walk/Assets/ThirdParty/ambientCG/Asphalt020S'
BASE = '57691931f92fac05831e3246239ae93489372fce'
MAPS = ('color', 'normalGL', 'roughness', 'ambientOcclusion')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(output):
    source_path = SOURCE_FOLDER / 'SOURCE.json'
    source = json.loads(source_path.read_text())
    status = json.loads((REPORTS / 'asphalt020s-status.json').read_text())
    validation_path = REPORTS / 'asphalt020s-validation.json'
    validation = json.loads(validation_path.read_text())
    assert source['asset'] == 'Asphalt020S' and source['license'] == 'CC0-1.0'
    assert source['sourceResolution'] == [2048, 2048]
    assert source['runtimeImportLimits'] == {'color': 2048, 'normalGL': 1024, 'packedMask': 1024}
    assert source['runtimeLevelZeroTexels'] == 6291456
    assert len(source['materialPaths']) == status['activeAsphaltMaterialCount'] == 7
    assert status['installed'] and status['baselineCommit'] == BASE
    assert status['independentValidation']['passed'] is True
    assert status['independentValidation']['sha256'] == digest(validation_path)
    assert status['sourceManifestSHA256'] == digest(source_path)
    assert validation and all(status['unchangedContent'].values())
    for key in MAPS:
        record = source['files'][key]
        assert digest(SOURCE_FOLDER / record['file']) == record['sha256']

    pdfmetrics.registerFont(TTFont('AsphaltSans', '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'))
    pdfmetrics.registerFont(TTFont('AsphaltBold', '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'))
    pdfmetrics.registerFont(TTFont('AsphaltMono', '/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf'))
    styles = getSampleStyleSheet()
    for key in ('Normal', 'BodyText', 'Heading1', 'Heading2', 'Title'):
        style = styles[key]
        style.fontName = 'AsphaltSans'
        style.fontSize = {'Title': 18, 'Heading1': 14, 'Heading2': 11}.get(key, 9)
        style.leading = {'Title': 23, 'Heading1': 18, 'Heading2': 15}.get(key, 13)
    styles['Heading2'].fontName = 'AsphaltBold'
    styles['Code'].fontName = 'AsphaltMono'
    styles['Code'].fontSize = 6.5
    styles['Code'].leading = 9
    flow = []

    def paragraph(value, style='BodyText'):
        flow.append(Paragraph(escape(str(value)), styles[style]))
        flow.append(Spacer(1, 5))

    def cell(value, mono=False):
        return Paragraph(escape(str(value)), styles['Code' if mono else 'BodyText'])

    def table(rows, widths):
        obj = Table(rows, colWidths=widths, hAlign='LEFT', repeatRows=1)
        obj.setStyle(TableStyle([
            ('VALIGN', (0, 0), (-1, -1), 'TOP'),
            ('BACKGROUND', (0, 0), (-1, 0), colors.HexColor('#edf1f5')),
            ('GRID', (0, 0), (-1, -1), .3, colors.HexColor('#b1b9c4')),
            ('LEFTPADDING', (0, 0), (-1, -1), 7),
            ('RIGHTPADDING', (0, 0), (-1, -1), 7),
            ('TOPPADDING', (0, 0), (-1, -1), 6),
            ('BOTTOMPADDING', (0, 0), (-1, -1), 6),
        ]))
        flow.extend([obj, Spacer(1, 10)])

    paragraph('Asphalt020S: оригинальные карты и материалы асфальта', 'Title')
    paragraph('15 ноября 2026 · hourly992/opd · черновой PR #3')
    paragraph('Ветка: codex/street-paving-leaves-garages. База PR: codex/painted-district. '
              'Исходный коммит этого обновления: ' + BASE)
    paragraph('Результат и источник', 'Heading2')
    paragraph('Установлен именно ambientCG Asphalt020S. Все семь активных материалов асфальта '
              'основной сцены используют Color, NormalGL и общую упакованную AO/roughness-карту. '
              'Другой материал вместо запрошенного не подбирался.')
    paragraph('Официальный источник: ' + source['source'])
    paragraph('Лицензия: Creative Commons CC0 1.0 Universal (CC0-1.0). ' + source['licenseUrl'])
    paragraph('Правовой текст: ' + source['legalCode'])
    paragraph('Официальная загрузка: ' + source['download'])
    paragraph('Фактическая доставка: пользовательский переупакованный архив '
              + source['archiveFileName'] + ', содержащий четыре исходных JPG. '
              'SHA-256 ниже относится к этому переданному контейнеру; он не выдан '
              'за checksum полного официального Asphalt020S_2K-JPG.zip.')
    paragraph('SHA-256 переданного архива: ' + source['archiveSHA256'], 'Code')
    paragraph('Четыре исходные карты', 'Heading2')
    paragraph('Все JPG имеют 2048 × 2048 пикселей и сохранены побайтно как в переданном ZIP. '
              'Имена и контрольные суммы записаны в установленном SOURCE.json.')
    rows = [[cell('Исходный файл'), cell('SHA-256')]]
    for key in MAPS:
        record = source['files'][key]
        rows.append([cell(record['file'], True), cell(record['sha256'], True)])
    table(rows, [225, 285])

    flow.append(PageBreak())
    paragraph('Материалы, упаковка и границы правки', 'Title')
    paragraph('Все активные материалы асфальта', 'Heading2')
    paragraph('Проверка проходит от Street.unity по ссылкам на prefab и материалы. '
              'Существующие GUID и слоты материалов сохраняются; изменены только '
              'их шейдер и свойства покрытия. Масштаб повторения — 1,85 × 1,85 м.')
    rows = [[cell('Материал относительно Unity_Walk/')]]
    rows.extend([[cell(path, True)] for path in source['materialPaths']])
    table(rows, [510])
    paragraph('Упакованная карта и импорт', 'Heading2')
    paragraph('AO_Roughness.png: R = красный канал исходной AmbientOcclusion, '
              'G = красный канал исходной Roughness, B = 0. Все 4 194 304 пикселя '
              'упакованы без изменения размера и перерисовки. Исходные AO и roughness JPG '
              'сохранены рядом, но не входят в runtime-зависимости материалов.')
    paragraph('Четыре исходных JPG и упакованный PNG остаются 2048². Unity импортирует '
              'Color с пределом 2048², NormalGL и упакованную маску — с пределом 1024². '
              'Снижение размера относится только к импортированным runtime-версиям normal '
              'и маски. NormalGL использует OpenGL/+Y; зелёный канал не разворачивается. '
              'Для карт заданы mipmaps, repeat wrap, правильный sRGB/linear-режим и instancing.')
    paragraph('Что сохранено', 'Heading2')
    paragraph('В этом обновлении не менялись файл сцены, геометрия, коллизии, дома, '
              'профиль улицы, красная плитка, белый бетон, листья и освещение. '
              'Ранее построенная сеть проездов и четыре гаражных въезда сохранены.')

    flow.append(PageBreak())
    paragraph('Проверка и производительность', 'Title')
    paragraph('Независимая проверка сохранённых данных', 'Heading2')
    paragraph('asphalt020s-validation.json сравнивает результат с ' + BASE + ': '
              'четыре JPG побайтно совпадают с содержимым пользовательского ZIP; '
              'все пиксели AO/roughness упакованы точно; все семь активных асфальтовых '
              'материалов ссылаются на требуемые карты и шейдер. Проверены настройки '
              'импорта и сохранность защищённых файлов.')
    paragraph('SHA-256 независимого отчёта: ' + digest(validation_path), 'Code')
    paragraph('SHA-256 установленного SOURCE.json: ' + digest(source_path), 'Code')
    paragraph('Граница выводов о производительности', 'Heading2')
    paragraph('В исходном коде нового surface-шейдера три texture fetch: color, normal '
              'и packed AO/roughness. Прежний общий шейдер содержит дополнительные '
              'чтения и операции на asphalt-path. Это статическая проверка исходного '
              'кода, не число команд GPU после компиляции всех lighting/shadow passes.')
    paragraph('Сумма texel уровня 0 для runtime-карт асфальта: 10 223 616 → 6 291 456 '
              'при заданных import caps (Color 2048² + NormalGL 1024² + Mask 1024²). '
              'Это счётчик ресурсов: формат сжатия, mip-цепочки, итоговая резидентность '
              'и общие карты других материалов могут влиять на фактическую память. '
              'Геометрия и сохранённые renderer/submesh ресурсы не изменены.')
    closure = validation['fullMaterialClosureTextureUnion']
    spaced = lambda value: f'{value:,}'.replace(',', ' ')
    paragraph('Полная цепочка материалов основной сцены и 47 prefab (344 материала): '
              f'уникальные texture references {closure["beforeUniqueTextures"]} → '
              f'{closure["afterUniqueTextures"]}; сумма эффективных texel уровня 0 '
              f'{spaced(closure["beforeLevelZeroTexels"])} → '
              f'{spaced(closure["afterLevelZeroTexels"])}. Включены прежние три карты '
              'WalkEdge, ещё используемые другими материалами. Это также оценка '
              'по ссылкам и настройкам импорта, не замер GPU-памяти.')
    paragraph('Измерения VRAM, FPS, GPU-времени, времени кадра и фактических draw calls '
              'не выполнялись. Эти счётчики не гарантируют отсутствие регрессии FPS.')
    paragraph('Ограничения и воспроизведение', 'Heading2')
    paragraph('Unity 6000.6.7f1 в среде отсутствует. Импорт шейдера, компиляция, сборка '
              'и запуск игры не выполнялись. Игровые скриншоты после сборки пользователь '
              'снимает самостоятельно; этот PDF не содержит игровых кадров.')
    paragraph('Установка: python Maintenance/StreetRepair/install_asphalt020s.py '
              '--archive /path/to/Asphalt020S_4maps.zip либо --maps-dir /path/to/maps. '
              'Проверка: тот же скрипт с --verify. Пересборка этого PDF: '
              'python Maintenance/StreetRepair/report_asphalt020s.py.')
    paragraph('Доказательства: установленный SOURCE.json, asphalt020s-status.json '
              'и asphalt020s-validation.json. Предыдущие PDF сохранены как история '
              'соответствующих этапов. PR остаётся черновиком; слияние не выполнялось.')

    def footer(pdf_canvas, doc):
        pdf_canvas.setFont('AsphaltSans', 7)
        pdf_canvas.setFillColor(colors.HexColor('#596473'))
        pdf_canvas.drawString(42, 22, 'Asphalt020S · PR #3 · 15.11.2026 · проверка сохранённых данных')
        pdf_canvas.drawRightString(A4[0] - 42, 22, str(doc.page))

    def deterministic_canvas(*args, **kwargs):
        # Platypus supplies invariant=None, which overrides functools.partial.
        kwargs['invariant'] = 1
        return canvas.Canvas(*args, **kwargs)

    output.parent.mkdir(parents=True, exist_ok=True)
    SimpleDocTemplate(
        str(output), pagesize=A4, rightMargin=42, leftMargin=42,
        topMargin=35, bottomMargin=38, title='Asphalt020S — исходные карты и материалы — PR #3',
        author='Codex',
    ).build(flow, onFirstPage=footer, onLaterPages=footer,
            canvasmaker=deterministic_canvas)
    pdf = PdfReader(str(output))
    extracted = '\n'.join(page.extract_text() for page in pdf.pages)
    assert len(pdf.pages) == 3, 'Report layout changed; inspect all pages before delivery'
    for expected in ('Asphalt020S', 'CC0-1.0', BASE, '6 291 456', 'сборка'):
        assert expected in extracted, ('Missing report content', expected)
    print(json.dumps(dict(output=str(output), pages=len(pdf.pages), textVerified=True), ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=REPORTS / 'Asphalt020S-2026-11-15.pdf')
    run(parser.parse_args().output)
