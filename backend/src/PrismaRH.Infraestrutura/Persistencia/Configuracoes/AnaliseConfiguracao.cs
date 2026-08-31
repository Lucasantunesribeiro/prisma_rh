using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Analises;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class RegraAnaliseConfiguracao : IEntityTypeConfiguration<RegraAnalise>
{
    public void Configure(EntityTypeBuilder<RegraAnalise> builder)
    {
        builder.ToTable("regras_analise");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(r => r.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();

        builder.Property(r => r.Codigo)
            .HasColumnName("codigo").HasConversion<int>().IsRequired();

        builder.Property(r => r.Ativa).HasColumnName("ativa").IsRequired();

        builder.Property(r => r.Severidade)
            .HasColumnName("severidade").HasConversion<int>().IsRequired();

        builder.Property(r => r.AlteradoEm).HasColumnName("alterado_em").IsRequired();
        builder.Property(r => r.AlteradoPor).HasColumnName("alterado_por");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(r => r.AlteradoPor)
            // Restrict: apagar um usuario nao apaga o registro de que ele
            // afrouxou uma regra de conferencia.
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(r => r.Parametros)
            .HasField("_parametros")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(r => r.Parametros)
            .WithOne()
            .HasForeignKey(p => p.IdRegraAnalise)
            .OnDelete(DeleteBehavior.Cascade);

        // Uma configuracao por regra, por organizacao. Duas linhas para a mesma
        // regra fariam o comportamento depender de qual delas o EF trouxesse
        // primeiro - defeito que so aparece com dado real.
        builder.HasIndex(r => new { r.IdOrganizacao, r.Codigo })
            .HasDatabaseName("ux_regras_analise_organizacao_codigo")
            .IsUnique();
    }
}

public sealed class ParametroRegraAnaliseConfiguracao
    : IEntityTypeConfiguration<ParametroRegraAnalise>
{
    public void Configure(EntityTypeBuilder<ParametroRegraAnalise> builder)
    {
        builder.ToTable("parametros_regra_analise");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(p => p.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(p => p.IdRegraAnalise).HasColumnName("id_regra_analise").IsRequired();

        builder.Property(p => p.Chave)
            .HasColumnName("chave").HasMaxLength(60).IsRequired();

        // Texto, e nao numeric: os tipos aceitos sao decimal, percentual e
        // inteiro, e uma coluna por tipo seria tres com duas sempre nulas. O
        // valor e revalidado contra a faixa declarada pela regra a cada
        // leitura - ver MotorAnalises.Interpretar.
        builder.Property(p => p.Valor)
            .HasColumnName("valor")
            .HasMaxLength(DefinicaoParametro.TamanhoMaximoValor)
            .IsRequired();

        builder.HasIndex(p => new { p.IdRegraAnalise, p.Chave })
            .HasDatabaseName("ux_parametros_regra_analise_chave")
            .IsUnique();
    }
}

public sealed class ExecucaoAnaliseConfiguracao : IEntityTypeConfiguration<ExecucaoAnalise>
{
    public void Configure(EntityTypeBuilder<ExecucaoAnalise> builder)
    {
        builder.ToTable("execucoes_analise");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(e => e.IdFolha).HasColumnName("id_folha").IsRequired();

        // Competencia vira int pelo mesmo conversor do resto do sistema.
        builder.Property(e => e.Competencia)
            .HasColumnName("competencia")
            .HasConversion(c => c.Codigo, codigo => Competencia.DoCodigo(codigo))
            .IsRequired();

        builder.Property(e => e.VersaoCalculoDaFolha)
            .HasColumnName("versao_calculo_folha").IsRequired();

        builder.Property(e => e.IdUsuario).HasColumnName("id_usuario");
        builder.Property(e => e.ExecutadaEm).HasColumnName("executada_em").IsRequired();

        builder.Property(e => e.RegrasExecutadas).HasColumnName("regras_executadas").IsRequired();
        builder.Property(e => e.TotalResultados).HasColumnName("total_resultados").IsRequired();
        builder.Property(e => e.ResultadosAltos).HasColumnName("resultados_altos").IsRequired();
        builder.Property(e => e.ResultadosMedios).HasColumnName("resultados_medios").IsRequired();
        builder.Property(e => e.ResultadosBaixos).HasColumnName("resultados_baixos").IsRequired();

        builder.HasOne<FolhaPagamento>()
            .WithMany()
            .HasForeignKey(e => e.IdFolha)
            // Cascade: apagar a folha apaga a analise dela. A analise nao tem
            // significado sozinha - ela e uma afirmacao SOBRE aquela folha.
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(e => e.IdUsuario)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(e => e.Resultados)
            .HasField("_resultados")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(e => e.Resultados)
            .WithOne()
            .HasForeignKey(r => r.IdExecucaoAnalise)
            .OnDelete(DeleteBehavior.Cascade);

        // "As analises desta folha, da mais recente para a mais antiga" e a
        // unica pergunta que a tela faz.
        builder.HasIndex(e => new { e.IdOrganizacao, e.IdFolha, e.ExecutadaEm })
            .HasDatabaseName("ix_execucoes_analise_folha")
            .IsDescending(false, false, true);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_execucoes_analise_contadores",
            "regras_executadas >= 0 and total_resultados >= 0 "
            + "and total_resultados = resultados_altos + resultados_medios + resultados_baixos"));
    }
}

public sealed class ResultadoAnaliseConfiguracao : IEntityTypeConfiguration<ResultadoAnalise>
{
    public void Configure(EntityTypeBuilder<ResultadoAnalise> builder)
    {
        builder.ToTable("resultados_analise");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(r => r.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(r => r.IdExecucaoAnalise).HasColumnName("id_execucao_analise").IsRequired();
        builder.Property(r => r.IdFolha).HasColumnName("id_folha").IsRequired();

        builder.Property(r => r.Codigo)
            .HasColumnName("codigo_regra").HasConversion<int>().IsRequired();

        builder.Property(r => r.VersaoRegra).HasColumnName("versao_regra").IsRequired();

        builder.Property(r => r.Categoria)
            .HasColumnName("categoria").HasConversion<int>().IsRequired();

        builder.Property(r => r.Severidade)
            .HasColumnName("severidade").HasConversion<int>().IsRequired();

        builder.Property(r => r.IdFolhaFuncionario).HasColumnName("id_folha_funcionario");
        builder.Property(r => r.IdFuncionario).HasColumnName("id_funcionario");

        builder.Property(r => r.Matricula).HasColumnName("matricula").HasMaxLength(30);
        builder.Property(r => r.NomeFuncionario).HasColumnName("nome_funcionario").HasMaxLength(200);

        builder.Property(r => r.Descricao)
            .HasColumnName("descricao")
            .HasMaxLength(ResultadoAnalise.TamanhoMaximoDescricao)
            .IsRequired();

        // numeric(14,2), como todo dinheiro do sistema. Nulavel porque nem todo
        // achado tem valor: "este desligado nao deveria estar aqui" nao tem
        // valor esperado, e um zero ali seria informacao falsa.
        builder.Property(r => r.ValorEsperado)
            .HasColumnName("valor_esperado").HasColumnType("numeric(14,2)");

        builder.Property(r => r.ValorEncontrado)
            .HasColumnName("valor_encontrado").HasColumnType("numeric(14,2)");

        builder.Property(r => r.Diferenca)
            .HasColumnName("diferenca").HasColumnType("numeric(14,2)");

        builder.Property(r => r.Contexto)
            .HasColumnName("contexto")
            .HasMaxLength(ResultadoAnalise.TamanhoMaximoContexto);

        // ------------------------------------------------------- Fase 7

        builder.Property(r => r.Status)
            .HasColumnName("status").HasConversion<int>().IsRequired();

        builder.Property(r => r.IdResponsavel).HasColumnName("id_responsavel");

        builder.Property(r => r.Justificativa)
            .HasColumnName("justificativa")
            .HasMaxLength(ResultadoAnalise.TamanhoMaximoJustificativa);

        builder.Property(r => r.ConcluidaEm).HasColumnName("concluida_em");

        builder.HasOne<PrismaRH.Dominio.Identidade.Usuario>()
            .WithMany()
            .HasForeignKey(r => r.IdResponsavel)
            // Restrict: apagar um usuario nao apaga a inconsistencia que estava
            // com ele. O trabalho fica sem dono, e nao sem registro.
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(r => r.Andamentos)
            .HasField("_andamentos")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(r => r.Andamentos)
            .WithOne()
            .HasForeignKey(a => a.IdResultadoAnalise)
            .OnDelete(DeleteBehavior.Cascade);

        // "As pendencias desta organizacao, das mais graves para as mais
        // leves" - a pergunta do painel e da caixa de trabalho.
        builder.HasIndex(r => new { r.IdOrganizacao, r.Status, r.Severidade })
            .HasDatabaseName("ix_resultados_analise_status")
            .IsDescending(false, false, true);

        builder.HasIndex(r => new { r.IdOrganizacao, r.IdResponsavel })
            .HasDatabaseName("ix_resultados_analise_responsavel");

        // SEM foreign key para folhas_funcionario de proposito.
        //
        // Recalcular uma folha recria os holerites com ids novos, e uma FK
        // faria o recalculo esbarrar nos resultados da analise anterior. O
        // vinculo existe para navegar da tela, e a analise velha continua
        // legivel mesmo apontando para um holerite que nao existe mais - ela e
        // registro do que foi visto naquele momento (`CLAUDE.md secao 4.3`).
        builder.HasIndex(r => new { r.IdOrganizacao, r.IdExecucaoAnalise, r.Severidade })
            .HasDatabaseName("ix_resultados_analise_execucao")
            .IsDescending(false, false, true);

        builder.HasIndex(r => new { r.IdOrganizacao, r.IdFolha })
            .HasDatabaseName("ix_resultados_analise_folha");
    }
}
