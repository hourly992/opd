# ambientCG Asphalt 020 S

Source: https://ambientcg.com/view?id=Asphalt020S
Official archive: https://ambientcg.com/get?file=Asphalt020S_2K-JPG.zip
License: Creative Commons CC0 1.0 Universal (CC0-1.0).
Official license statement: https://docs.ambientcg.com/license/
Legal code: https://creativecommons.org/publicdomain/zero/1.0/

Created using Asphalt 020 S from ambientCG.com, licensed under the Creative
Commons CC0 1.0 Universal License. The source permits copying, modifying and
redistributing the raw maps, including commercial game projects.

The four original 2K JPEG maps are preserved byte-for-byte. NormalGL uses the
OpenGL/+Y convention (Unity normal importer, no green-channel flip). The shared
runtime mask file stores source AO in red and source roughness in green, without
resampling its pixels. The original JPEG files remain 2048 x 2048. Unity imports
color at up to 2048, and normal plus packed mask at up to 1024 to keep the runtime
texture budget below the former asphalt set. It uses three texture reads (color, normal, packed AO/roughness),
physical repeat 1.85 × 1.85 m, mipmaps, repeat wrap and instancing. Source AO and
roughness remain beside it. Unreferenced originals are not runtime dependencies.
SOURCE.json records the original file SHA-256 checksums and delivery method.
When a ZIP was supplied, its name and checksum identify that input container,
which may be a user-created four-map repack rather than the full official ZIP.
With separately supplied original maps archiveSHA256 is null. No other texture was selected or
synthesized for this replacement.
