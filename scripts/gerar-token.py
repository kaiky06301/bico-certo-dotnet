#!/usr/bin/env python3
"""
Gera um token JWT igual ao emitido pela API Java do Bico Certo (HS256, issuer bico-certo-api,
claims sub/uid/papel). Serve para testar a API .NET sozinha (ex.: no deploy da DevOps).

Uso:
    export JWT_SECRET="mesmo segredo configurado em Jwt__Secret"
    python3 scripts/gerar-token.py --papel PRESTADOR --uid 5 --email carlos@bicocerto.com.br
"""
import argparse
import base64
import hashlib
import hmac
import json
import os
import sys
import time


def b64url(dados: bytes) -> str:
    return base64.urlsafe_b64encode(dados).rstrip(b"=").decode()


def main() -> None:
    parser = argparse.ArgumentParser(description="Gera JWT de teste para a Contratacoes API")
    parser.add_argument("--papel", required=True, choices=["CLIENTE", "PRESTADOR", "ADMIN"])
    parser.add_argument("--uid", required=True, type=int, help="id do usuario")
    parser.add_argument("--email", default=None)
    parser.add_argument("--minutos", type=int, default=60)
    args = parser.parse_args()

    segredo = os.environ.get("JWT_SECRET", "")
    if len(segredo.encode()) < 32:
        sys.exit("Defina a variavel JWT_SECRET (32+ caracteres), a mesma de Jwt__Secret da API.")

    agora = int(time.time())
    cabecalho = {"alg": "HS256", "typ": "JWT"}
    corpo = {
        "sub": args.email or f"usuario{args.uid}@bicocerto.com.br",
        "uid": args.uid,
        "papel": args.papel,
        "iss": "bico-certo-api",
        "iat": agora,
        "exp": agora + args.minutos * 60,
    }
    parte = b64url(json.dumps(cabecalho, separators=(",", ":")).encode()) + "." + \
        b64url(json.dumps(corpo, separators=(",", ":")).encode())
    assinatura = hmac.new(segredo.encode(), parte.encode(), hashlib.sha256).digest()
    print(parte + "." + b64url(assinatura))


if __name__ == "__main__":
    main()
