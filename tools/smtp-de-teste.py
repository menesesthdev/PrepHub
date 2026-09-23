#!/usr/bin/env python3
"""
Servidor SMTP de teste para desenvolvimento — imprime no terminal o e-mail que a aplicação
tentou enviar, em vez de entregá-lo a alguém.

Serve para exercitar o caminho SMTP de verdade (EnviadorDeEmailSmtp) sem precisar de conta em
provedor nenhum. Sem ele, o padrão da aplicação já é o EnviadorDeEmailParaLog, que escreve a
mensagem no log — este script é para quando você quer testar o envio em si.

Uso:
    python3 tools/smtp-de-teste.py

    # noutro terminal, apontando a app para cá (só nesta execução):
    Email__SmtpHost=127.0.0.1 Email__SmtpPort=1025 Email__UsarSsl=false \
        ~/.dotnet/dotnet run --project src/PrepHub.Web

⚠️ Só para desenvolvimento: aceita qualquer remetente, não valida nada, não tem TLS e escuta
apenas em 127.0.0.1. Não é para rodar em máquina exposta.

Implementa o mínimo do SMTP (RFC 5321) que o SmtpClient do .NET usa: EHLO, MAIL FROM, RCPT TO,
DATA, QUIT — mais AUTH, para o caso de você configurar usuário e senha e querer ver passar.
"""

import re
import socketserver
import sys
from datetime import datetime
from email import message_from_string
from email.header import decode_header, make_header

HOST = "127.0.0.1"
PORT = 1025


class ManipuladorSmtp(socketserver.StreamRequestHandler):
    """Uma conexão SMTP. O SmtpClient abre uma por envio."""

    def handle(self):
        self._responder(220, "PrepHub SMTP de teste pronto")

        remetente = None
        destinatarios = []

        while True:
            linha = self.rfile.readline()
            if not linha:
                return

            comando = linha.decode("utf-8", "replace").rstrip("\r\n")
            verbo = comando.split(" ")[0].upper() if comando else ""

            if verbo in ("EHLO", "HELO"):
                # Sem STARTTLS na lista de propósito: anunciar o que não temos faria o
                # cliente tentar negociar TLS e a conexão morrer no meio.
                self.wfile.write(b"250-PrepHub SMTP de teste\r\n")
                self.wfile.write(b"250-AUTH PLAIN LOGIN\r\n")
                self.wfile.write(b"250 8BITMIME\r\n")

            elif verbo == "AUTH":
                # Aceita qualquer credencial — o ponto aqui é ver o fluxo, não autenticar.
                if comando.upper().startswith("AUTH LOGIN") and len(comando.split()) == 2:
                    self._responder(334, "VXNlcm5hbWU6")  # "Username:" em Base64
                    self.rfile.readline()
                    self._responder(334, "UGFzc3dvcmQ6")  # "Password:"
                    self.rfile.readline()
                self._responder(235, "Autenticação aceita (servidor de teste)")

            elif verbo == "MAIL":
                remetente = self._extrair_endereco(comando)
                self._responder(250, "Remetente aceito")

            elif verbo == "RCPT":
                destinatarios.append(self._extrair_endereco(comando))
                self._responder(250, "Destinatário aceito")

            elif verbo == "DATA":
                self._responder(354, "Manda o conteúdo, termina com <CRLF>.<CRLF>")
                self._imprimir(remetente, destinatarios, self._ler_mensagem())
                self._responder(250, "Mensagem recebida")
                remetente, destinatarios = None, []

            elif verbo == "RSET":
                remetente, destinatarios = None, []
                self._responder(250, "Estado limpo")

            elif verbo == "NOOP":
                self._responder(250, "Ok")

            elif verbo == "QUIT":
                self._responder(221, "Até logo")
                return

            else:
                self._responder(502, f"Comando não implementado: {verbo}")

    def _ler_mensagem(self) -> str:
        """Lê até o ponto sozinho numa linha, desfazendo o dot-stuffing do protocolo."""
        linhas = []
        while True:
            linha = self.rfile.readline()
            if not linha or linha in (b".\r\n", b".\n"):
                break

            texto = linha.decode("utf-8", "replace").rstrip("\r\n")
            # O SMTP dobra o ponto inicial para ele não ser confundido com o fim da mensagem.
            linhas.append(texto[1:] if texto.startswith("..") else texto)

        return "\n".join(linhas)

    def _imprimir(self, remetente, destinatarios, mensagem):
        agora = datetime.now().strftime("%H:%M:%S")

        # O .NET manda assunto e corpo codificados (Base64 ou quoted-printable) quando há
        # acento, então imprimir cru sairia ilegível. O parser da stdlib desfaz as duas coisas.
        try:
            parsed = message_from_string(mensagem)
            assunto = str(make_header(decode_header(parsed.get("Subject", ""))))
            corpo = self._corpo_de(parsed)
        except Exception:  # noqa: BLE001 - servidor de teste: nunca deve morrer por e-mail torto
            assunto, corpo = "(não foi possível decodificar)", mensagem

        print(f"\n{'=' * 78}")
        print(f"[{agora}] E-mail recebido")
        print(f"De:      {remetente}")
        print(f"Para:    {', '.join(d for d in destinatarios if d)}")
        print(f"Assunto: {assunto}")
        print("-" * 78)
        print(corpo)

        # Atalho para o que interessa neste projeto: o link de redefinição.
        for url in re.findall(r"https?://\S+", corpo):
            print(f"\n>>> LINK: {url}")

        print(f"{'=' * 78}\n", flush=True)

    @staticmethod
    def _corpo_de(parsed) -> str:
        """Primeira parte de texto da mensagem, já decodificada."""
        parte = parsed
        if parsed.is_multipart():
            parte = next(
                (p for p in parsed.walk() if p.get_content_type() == "text/plain"),
                None,
            )
            if parte is None:
                return "(sem parte text/plain)"

        bruto = parte.get_payload(decode=True)
        if bruto is None:
            return str(parte.get_payload())

        return bruto.decode(parte.get_content_charset() or "utf-8", "replace")

    @staticmethod
    def _extrair_endereco(comando: str) -> str | None:
        entre_angulos = re.search(r"<([^>]*)>", comando)
        return entre_angulos.group(1) if entre_angulos else None

    def _responder(self, codigo: int, texto: str):
        self.wfile.write(f"{codigo} {texto}\r\n".encode())

    # Silencia o log de erro padrão do socketserver: cliente que fecha a conexão de
    # supetão é normal aqui e não é problema nenhum.
    def handle_error(self, *_):
        pass


class ServidorSmtp(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


if __name__ == "__main__":
    print(f"SMTP de teste escutando em {HOST}:{PORT} — Ctrl+C para parar.")
    print("Aponte a aplicação com:")
    print("  Email__SmtpHost=127.0.0.1 Email__SmtpPort=1025 Email__UsarSsl=false\n", flush=True)

    try:
        with ServidorSmtp((HOST, PORT), ManipuladorSmtp) as servidor:
            servidor.serve_forever()
    except KeyboardInterrupt:
        print("\nEncerrado.")
        sys.exit(0)
