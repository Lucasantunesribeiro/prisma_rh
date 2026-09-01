using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class FolhaPagamentoConfiguracao : IEntityTypeConfiguration<FolhaPagamento>
{
    public void Configure(EntityTypeBuilder<FolhaPagamento> builder)
    {
        builder.ToTable("folhas_pagamento");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(f => f.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(f => f.IdEmpresa).HasColumnName("id_empresa").IsRequired();

        // Competencia vira o inteiro 202608. Uma coluna so, ordenavel e
        // indexavel: 202512 < 202601 sem conversao nenhuma. Duas colunas
        // exigiriam ORDER BY ano, mes em toda consulta, e bastaria esquecer o
        // segundo campo para janeiro aparecer antes de dezembro.
        builder.Property(f => f.Competencia)
            .HasColumnName("competencia")
            .HasConversion(c => c.Codigo, codigo => Competencia.DoCodigo(codigo))
            .IsRequired();

        builder.Property(f => f.Situacao).HasColumnName("situacao").HasConversion<int>().IsRequired();
        builder.Property(f => f.VersaoCalculo).HasColumnName("versao_calculo").IsRequired();
        builder.Property(f => f.CriadoEm).HasColumnName("criado_em").IsRequired();
        builder.Property(f => f.CalculadaEm).HasColumnName("calculada_em");
        builder.Property(f => f.FechadaEm).HasColumnName("fechada_em");

        builder.Property(f => f.TotalProventos).HasColumnName("total_proventos").HasPrecision(14, 2).IsRequired();
        builder.Property(f => f.TotalDescontos).HasColumnName("total_descontos").HasPrecision(14, 2).IsRequired();
        builder.Property(f => f.TotalLiquido).HasColumnName("total_liquido").HasPrecision(14, 2).IsRequired();

        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(f => f.IdEmpresa)
            .OnDelete(DeleteBehavior.Restrict);

        // ⚠️ O `HasDefaultValue(TipoFolha.Mensal)` foi REMOVIDO na revisao
        // pos-roadmap, em 01/09/2026.
        //
        // Ele existiu para uma coisa so: o BACKFILL da Fase 4E, marcando como
        // mensais as folhas criadas antes de o tipo existir. Esse trabalho
        // terminou naquela migration e nao se repete.
        //
        // O que ele continuava fazendo era emitir um aviso do EF em TODA
        // execucao de `dotnet ef` - "configured with a database-generated
        // default, but has no configured sentinel value". O aviso era inofensivo
        // (TipoFolha nao tem membro 0, e o construtor sempre define o valor),
        // mas aviso permanente ensina a ignorar aviso, e ai o proximo - que
        // importa - passa junto.
        builder.Property(f => f.Tipo)
            .HasColumnName("tipo").HasConversion<int>().IsRequired();

        // Uma folha por empresa, por competencia E POR TIPO. Abrir agosto
        // duas vezes do mesmo tipo produziria dois totais divergentes para o
        // mesmo mes, e nada no sistema diria qual e o verdadeiro.
        //
        // A coluna do tipo entrou na Fase 4E: a mesma empresa pode ter, em
        // agosto, a folha mensal E a de ferias. Sem ela, a segunda seria
        // recusada por um indice que nao existia para isso.
        builder.HasIndex(f => new { f.IdEmpresa, f.Competencia, f.Tipo })
            .HasDatabaseName("ux_folhas_empresa_competencia_tipo")
            .IsUnique();

        builder.HasMany(f => f.Funcionarios)
            .WithOne()
            .HasForeignKey(ff => ff.IdFolha)
            .OnDelete(DeleteBehavior.Cascade);

        // O EF le e escreve a colecao pelo CAMPO, nunca pela propriedade. A
        // propriedade e somente leitura e existe para o resto do sistema; se o
        // EF tentasse usa-la para adicionar um item, nao teria onde adicionar.
        builder.Navigation(f => f.Funcionarios)
            .HasField("_funcionarios")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
