#!/usr/bin/env bash
#
# Regenera os arquivos de marca servidos em wwwroot/ a partir das fontes desta pasta.
# Rode depois de trocar a arte original — os derivados NÃO devem ser editados à mão.
#
# Requer apenas ffmpeg e python3 (o .ico é montado byte a byte mais abaixo, sem depender
# de ImageMagick nem de Pillow).
#
set -euo pipefail

AQUI="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WWWROOT="$(cd "$AQUI/../.." && pwd)/src/PrepHub.Web/wwwroot"

# Duas fontes, cada uma para um uso: a marca sozinha (nuvem + "P" + checklist) vira os ícones,
# e a logo completa, com "PREPHUB APP" e o descritivo, vai para as telas de conta. Os textos
# ficam fora dos ícones de propósito: viram borrão ilegível em 16px.
MARCA="$AQUI/logo-prephub-marca.png"
COMPLETA="$AQUI/logo-prephub-completa.png"

# Os recortes vêm da área com alpha > 0 de cada arte, com ~12px de folga. A marca (597x418)
# ocupa x 61–534 e y 41–364: recorta 497x335 e centraliza num quadrado transparente.
RECORTE_MARCA="crop=497:335:49:35,pad=497:497:0:81:color=#00000000"

# A completa (1536x1024) ocupa x 258–1284 e y 77–931. O recorte 1056x880 é 6:5 exato — a mesma
# proporção da caixa .login__logo no CSS, então a imagem não é distorcida nem cortada lá.
RECORTE_COMPLETA="crop=1056:880:240:64"

mkdir -p "$WWWROOT/img"

# Ícones quadrados. O 48 só existe para entrar no .ico e é descartado no final.
for TAMANHO in 16 32 48 180 192 512; do
    ffmpeg -y -v error -i "$MARCA" \
        -vf "$RECORTE_MARCA,scale=$TAMANHO:$TAMANHO:flags=lanczos" \
        "$WWWROOT/img/icone-$TAMANHO.png"
done

# Logo completa das telas de conta: exibida a 240x200, gerada em 2x para telas densas.
ffmpeg -y -v error -i "$COMPLETA" -vf "$RECORTE_COMPLETA,scale=480:400:flags=lanczos" \
    "$WWWROOT/img/logo-prephub.png"

# favicon.ico multi-resolução. O formato aceita PNG embutido desde o Vista, então basta
# o cabeçalho ICONDIR (6 bytes) + uma ICONDIRENTRY (16 bytes) por imagem + os blobs.
python3 - "$WWWROOT" <<'PYTHON'
import struct
import sys

wwwroot = sys.argv[1]
tamanhos = [16, 32, 48]

pngs = []
for tamanho in tamanhos:
    with open(f"{wwwroot}/img/icone-{tamanho}.png", "rb") as arquivo:
        pngs.append((tamanho, arquivo.read()))

cabecalho = struct.pack("<HHH", 0, 1, len(pngs))
deslocamento = len(cabecalho) + 16 * len(pngs)

entradas, blobs = b"", b""
for tamanho, dados in pngs:
    entradas += struct.pack(
        "<BBBBHHII",
        tamanho if tamanho < 256 else 0,  # largura (0 significa 256)
        tamanho if tamanho < 256 else 0,  # altura
        0,                                # paleta indexada: nenhuma
        0,                                # reservado
        1,                                # planos de cor
        32,                               # bits por pixel
        len(dados),
        deslocamento,
    )
    blobs += dados
    deslocamento += len(dados)

with open(f"{wwwroot}/favicon.ico", "wb") as arquivo:
    arquivo.write(cabecalho + entradas + blobs)

print(f"favicon.ico: {len(cabecalho + entradas + blobs)} bytes, {len(pngs)} resolucoes")
PYTHON

rm -f "$WWWROOT/img/icone-48.png" "$WWWROOT/img/logo-prephub.png"

echo "Derivados regenerados em $WWWROOT"
