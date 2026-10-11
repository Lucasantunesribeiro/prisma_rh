"""Empacota a API ou o worker do Prisma RH para a AWS Lambda (provided.al2023).

Uso, a partir da raiz do repositorio:

    python scripts/empacotar-lambda.py api
    python scripts/empacotar-lambda.py worker

Gera `.publicacao/<projeto>.zip` e imprime tamanho e SHA-256. NAO chama a AWS:
empacotar e publicar sao passos separados, e o procedimento de publicacao, com a
reversao de cada passo, esta em docs/publicacao.md.

Por que Python, e nao Compress-Archive nem System.IO.Compression
-----------------------------------------------------------------
A Lambda executa um arquivo chamado `bootstrap`, e ele precisa do bit de
execucao. Duas armadilhas ja custaram producao fora do ar (ROADMAP.md, secao
10.9):

1. publicar sem `-p:PublicarLambda=true` gera um executavel WINDOWS, que a
   Lambda recusa com `Runtime.InvalidEntrypoint`;
2. zip montado no Windows grava o "sistema de origem" como MS-DOS, e quem
   descompacta no Linux IGNORA as permissoes Unix desse tipo de arquivo - o
   `bootstrap` chega sem permissao de execucao.

Este script trata as duas: recusa continuar se o `bootstrap` nao for um ELF
(binario Linux) e grava o zip declarando origem Unix, com modo 0755 explicito.
"""

from __future__ import annotations

import hashlib
import os
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

PROJETOS = {
    "api": {
        "csproj": "backend/src/PrismaRH.Api/PrismaRH.Api.csproj",
        # Sem esta propriedade sai um executavel Windows chamado PrismaRH.Api.
        "extras": ["-p:PublicarLambda=true"],
    },
    "worker": {
        "csproj": "backend/src/PrismaRH.Worker/PrismaRH.Worker.csproj",
        # O worker ja fixa bootstrap/linux-x64/self-contained no proprio csproj.
        "extras": [],
    },
}

# Configuracao de desenvolvimento nao vai para a Lambda. Em producao ela nem e
# lida (o ambiente e Production), mas carrega a chave JWT e a senha do banco
# LOCAIS - nao ha motivo para publicar arquivo com credencial, mesmo de
# brinquedo.
FORA_DO_PACOTE = {"appsettings.Development.json"}

ELF = b"\x7fELF"
UNIX = 3  # "version made by" do formato ZIP: 3 = Unix
EXECUTAVEL = 0o100755
COMUM = 0o100644


def dotnet() -> str:
    """O dotnet que atende o global.json (10.0.4xx), mesmo fora do PATH."""
    candidatos = []
    if os.environ.get("DOTNET_ROOT"):
        candidatos.append(Path(os.environ["DOTNET_ROOT"]) / ("dotnet.exe" if os.name == "nt" else "dotnet"))
    candidatos.append(Path.home() / ".dotnet" / ("dotnet.exe" if os.name == "nt" else "dotnet"))
    for candidato in candidatos:
        if candidato.exists():
            return str(candidato)
    encontrado = shutil.which("dotnet")
    if not encontrado:
        sys.exit("dotnet nao encontrado. Instale o SDK fixado em global.json.")
    return encontrado


def main() -> None:
    if len(sys.argv) != 2 or sys.argv[1] not in PROJETOS:
        sys.exit(f"uso: python {sys.argv[0]} api|worker")

    nome = sys.argv[1]
    projeto = PROJETOS[nome]
    raiz = Path(__file__).resolve().parent.parent
    destino = raiz / ".publicacao"
    publicado = destino / nome
    pacote = destino / f"{nome}.zip"

    shutil.rmtree(publicado, ignore_errors=True)
    destino.mkdir(exist_ok=True)

    comando = [
        dotnet(), "publish", str(raiz / projeto["csproj"]),
        "-c", "Release", "-r", "linux-x64", "-o", str(publicado),
        *projeto["extras"],
    ]
    print("$", " ".join(comando), flush=True)
    subprocess.run(comando, check=True, cwd=raiz)

    bootstrap = publicado / "bootstrap"
    if not bootstrap.exists():
        sys.exit(f"RECUSADO: {bootstrap} nao existe. O publish nao gerou o executavel da Lambda.")
    with bootstrap.open("rb") as arquivo:
        if arquivo.read(4) != ELF:
            sys.exit("RECUSADO: o bootstrap nao e um binario Linux (ELF). Nada foi empacotado.")

    if pacote.exists():
        pacote.unlink()

    with zipfile.ZipFile(pacote, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zip_saida:
        for caminho in sorted(p for p in publicado.rglob("*") if p.is_file()):
            relativo = caminho.relative_to(publicado).as_posix()
            if relativo in FORA_DO_PACOTE:
                print(f"fora do pacote: {relativo}")
                continue
            entrada = zipfile.ZipInfo(relativo, date_time=(2026, 1, 1, 0, 0, 0))
            entrada.create_system = UNIX
            entrada.external_attr = (EXECUTAVEL if relativo == "bootstrap" else COMUM) << 16
            entrada.compress_type = zipfile.ZIP_DEFLATED
            zip_saida.writestr(entrada, caminho.read_bytes())

    conferir(pacote)

    resumo = hashlib.sha256(pacote.read_bytes()).hexdigest()
    print(f"pacote: {pacote}")
    print(f"tamanho: {pacote.stat().st_size} bytes")
    print(f"sha256: {resumo}")


def conferir(pacote: Path) -> None:
    """Relê o zip gerado: o que importa e o que a Lambda vai encontrar."""
    with zipfile.ZipFile(pacote) as zip_lido:
        entrada = zip_lido.getinfo("bootstrap")
        modo = entrada.external_attr >> 16
        if entrada.create_system != UNIX or modo != EXECUTAVEL:
            sys.exit(f"RECUSADO: bootstrap no zip com origem {entrada.create_system} e modo {oct(modo)}.")
        if zip_lido.read("bootstrap")[:4] != ELF:
            sys.exit("RECUSADO: o bootstrap dentro do zip nao e ELF.")
        vazou = FORA_DO_PACOTE.intersection(zip_lido.namelist())
        if vazou:
            sys.exit(f"RECUSADO: {sorted(vazou)} entrou no pacote.")
    print("conferido: bootstrap ELF, origem Unix, modo 0755")


if __name__ == "__main__":
    main()
