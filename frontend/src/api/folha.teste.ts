import { describe, expect, it } from 'vitest'
import { competenciaPorExtenso, normalizarCompetencia, podeProcessarFolha } from './folha'

describe('normalizarCompetencia', () => {
  it.each([
    ['08/2026', '08/2026'],
    ['8/2026', '08/2026'],
    ['082026', '08/2026'],
    ['12/2026', '12/2026'],
    ['1/2030', '01/2030'],
  ])('aceita %s e devolve %s', (entrada, esperado) => {
    expect(normalizarCompetencia(entrada)).toBe(esperado)
  })

  it.each([
    ['', 'vazio'],
    ['agosto', 'sem dígito'],
    ['13/2026', 'mês inexistente'],
    ['00/2026', 'mês zero'],
    ['08/1999', 'ano fora da faixa'],
    ['2026', 'só o ano'],
    ['08/20260', 'dígitos demais'],
  ])('recusa %s (%s)', (entrada) => {
    expect(normalizarCompetencia(entrada)).toBeNull()
  })
})

describe('competenciaPorExtenso', () => {
  it('escreve o mês por extenso', () => {
    expect(competenciaPorExtenso('08/2026')).toBe('agosto de 2026')
    expect(competenciaPorExtenso('01/2026')).toBe('janeiro de 2026')
    expect(competenciaPorExtenso('12/2026')).toBe('dezembro de 2026')
  })

  it('devolve o original quando não reconhece', () => {
    expect(competenciaPorExtenso('99/2026')).toBe('99/2026')
  })
})

describe('podeProcessarFolha', () => {
  it('libera quem processa folha', () => {
    expect(podeProcessarFolha('AdministradorPlataforma')).toBe(true)
    expect(podeProcessarFolha('AdministradorEmpresa')).toBe(true)
    expect(podeProcessarFolha('AnalistaRh')).toBe(true)
  })

  it('barra quem só consulta', () => {
    // Auditor consulta folha e memória de cálculo, mas não altera dados
    // operacionais (CLAUDE.md seção 6).
    expect(podeProcessarFolha('Auditor')).toBe(false)
    expect(podeProcessarFolha('Visualizador')).toBe(false)
    expect(podeProcessarFolha(undefined)).toBe(false)
  })
})
