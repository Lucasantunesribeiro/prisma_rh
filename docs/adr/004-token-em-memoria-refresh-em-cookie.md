# ADR 004 — Access token em memória, refresh opaco em cookie

**Status:** aceita · **Data:** 2026-08 (Fase 1)

## Contexto

Uma SPA precisa manter sessão entre recarregamentos sem deixar a credencial ao alcance de
um XSS.

## Decisão

Duas credenciais, com propriedades opostas:

| | Access token | Refresh token |
|---|---|---|
| Formato | JWT assinado | **opaco** (sem conteúdo) |
| Vida | 15 minutos | 7 dias |
| Onde vive | **memória do JavaScript** | cookie `httpOnly`, `Secure`, `Path` restrito |
| No banco | não existe | guardado como **hash** |
| Vai sozinho na requisição? | não — header `Authorization` | sim, o navegador envia |

Mais: **rotação** a cada renovação, e **detecção de reúso** — um token já usado que
reaparece derruba **todas** as sessões daquele usuário. `ClockSkew = TimeSpan.Zero`, senão
os 15 minutos viveriam 20.

## O que foi recusado

**Access token em `localStorage`.** É a solução mais comum e a pior: `localStorage` é
legível por qualquer script da página. Um XSS rouba a sessão inteira.

**Refresh como JWT.** Um JWT carrega conteúdo e é válido até expirar — revogar exige uma
lista de bloqueio, ou seja, estado no servidor de qualquer jeito. Sendo opaco, ele já é
uma linha no banco: revogar é `UPDATE`.

**Refresh em texto puro no banco.** Vazamento do banco entregaria sessões ativas. Guardado
como hash, o vazamento não entrega nada — a forma bruta só existe no cookie do usuário.

**Ignorar o reúso.** Reúso de refresh é sinal de roubo. A resposta certa é encerrar tudo,
não renovar de novo.

## Consequências

**Boas:** um XSS não rouba a sessão; um vazamento de banco não rouba sessões; um token
capturado vale 15 minutos.

**Ruins:** ao recarregar a página o app precisa chamar `renovar` antes da primeira
requisição — há um instante sem token, e o frontend precisa lidar com isso.

**A consequência que quase passou despercebida:** cookie e produção não se dão bem. Com
frontend na Vercel e API no AWS Lambda, os domínios registráveis são diferentes, e
`SameSite=Lax` **não envia o cookie** — a sessão não sobreviveria a um recarregamento. A
correção óbvia, `SameSite=None`, reabre o CSRF que o `Lax` fechava de graça. Ver
`GuardaCsrf`: *double submit* comparado em tempo constante **mais** validação de `Origin`,
onde ausência é recusa.
