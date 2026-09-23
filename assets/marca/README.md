# Marca — arquivos-fonte

Arte original do PrepHub. **Nada aqui é servido pela aplicação**: estes arquivos existem
para gerar os derivados que ficam em `src/PrepHub.Web/wwwroot/`.

| Arquivo | Formato | Papel |
|---|---|---|
| `logo-prephub-marca.png` | 597×418 RGBA | Só o símbolo (nuvem + "P" + checklist). Origem dos ícones |
| `logo-prephub-completa.png` | 1536×1024 RGBA | Símbolo + "PREPHUB APP" + "simulados para exames Azure e AWS". Origem da logo das telas de conta |

⚠️ Ao abrir a logo completa num visualizador que ignora transparência, ela parece ter um brilho
azul sobre fundo preto e o texto quase some. É só o canal alpha sendo desenhado sem fundo: sobre
branco, que é onde a aplicação a mostra, o texto aparece nítido.

## Regenerar os derivados

```bash
./assets/marca/gerar-derivados.sh
```

Produz, em `wwwroot/`:

- `favicon.ico` — 16/32/48 embutidos, atende o pedido automático do browser por `/favicon.ico`
- `img/icone-{16,32,180,192,512}.png` — favicons, `apple-touch-icon` e a marca do cabeçalho
- `img/logo-prephub.png` — logo das telas de conta (480×400, exibida a 240×200)

Os derivados são gerados, não editados à mão: qualquer ajuste manual some na próxima execução.
Os recortes do script vêm da área visível de cada arte — trocar a arte exige recalculá-los.

## Por que ficam fora de `wwwroot/`

Servir a fonte não faria sentido — a logo completa tem mais de 1 MB, contra algumas dezenas
de KB do derivado que a tela de login realmente usa. Também não podem morar em `Views/`: ali só
os `.cshtml` são compilados, então imagens naquela pasta ficam inacessíveis pela web.
