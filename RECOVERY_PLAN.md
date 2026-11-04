# PLANO DE IMPLEMENTAÇÃO — RECUPERAÇÃO AVANÇADA DE BANCOS FIREBIRD 2.5

**Projeto:** ConsultaSQL  
**Módulo:** Banco > Manutenção > Recuperar banco corrompido  
**Compatibilidade:** Firebird 2.5.x — ODS 11.2  
**Prioridade:** Alta  
**Objetivo:** Evoluir o assistente existente para uma solução de recuperação segura, progressiva, auditável e capaz de preservar a maior quantidade possível de dados, mesmo quando o backup convencional falhar.

---

## 1. Objetivo do desenvolvimento

Aprimorar o assistente de recuperação do ConsultaSQL para que consiga identificar diferentes tipos de corrupção, selecionar procedimentos adequados, executar tentativas controladas e validar o banco recuperado.

A ferramenta deve trabalhar com diferentes cenários:

- Bancos que apresentam erros de índice.
- Bancos com páginas de dados danificadas.
- Bancos com registros ou BLOBs corrompidos.
- Bancos com problemas de integridade referencial.
- Bancos cujo backup falha mesmo após o `gfix -mend`.
- Bancos que permitem apenas recuperação parcial dos registros.
- Bancos com metadados incompletos ou danificados.

O objetivo não é apenas conseguir executar um restore. O programa deve identificar o que conseguiu recuperar, quais problemas permaneceram e quais informações podem ter sido perdidas.

**A recuperação nunca deve ser declarada completa apenas porque o backup ou o restore terminou sem erro.**

Também não deve existir a promessa de recuperação integral para qualquer corrupção física. O sistema precisa reconhecer seus limites e informar quando a integridade não puder ser comprovada.

## 2. Regras obrigatórias de segurança

Estas regras devem ser implementadas antes de qualquer nova rotina de reparo.

### 2.1. Preservação do banco original

O banco original nunca poderá receber comandos que alterem seu conteúdo ou sua estrutura.

Procedimentos como `gfix -mend`, restauração, desativação de índices, atualização de registros e exclusão de objetos devem ser executados exclusivamente em cópias.

A aplicação deverá:

1. Identificar o arquivo original e os arquivos associados.
2. Garantir que o banco esteja fora de uso e em condição segura para a cópia.
3. Calcular o hash SHA-256 do original.
4. Criar uma cópia de trabalho.
5. Verificar a integridade dessa cópia por hash, quando aplicável.
6. Recalcular o hash do original após a cópia e ao encerrar a execução, quando estiver acessível.
7. Bloquear a recuperação se as condições de consistência da cópia não puderem ser garantidas.

O original não deve ser aberto pelo assistente por meio de uma conexão normal do Firebird, pois uma conexão pode provocar alterações internas no banco.

A leitura direta do cabeçalho e o cálculo de hash são permitidos, desde que não alterem o arquivo.

**Importante:** testar apenas `FileShare.None` não garante que não existam conexões abertas pelo servidor Firebird. A implementação precisa controlar o acesso ao banco e confirmar que a cópia foi produzida em uma condição consistente.

### 2.2. Operações destrutivas

As seguintes ações exigem autorização explícita:

- Executar `gfix -mend`.
- Remover ou recriar constraints.
- Descartar registros irrecuperáveis.
- Substituir valores corrompidos por `NULL`.
- Ignorar tabelas inteiras.
- Finalizar uma recuperação incompleta.
- Tomar decisões de commit ou rollback sobre transações em limbo.

As confirmações precisam explicar os riscos em linguagem clara e ser registradas no manifesto.

### 2.3. Isolamento do resultado

O banco recuperado será sempre criado com outro nome e em um diretório separado.

O programa nunca deverá substituir automaticamente o banco em produção.

Arquivos incompletos deverão permanecer em staging, sem serem apresentados como bancos prontos para uso.

## 3. Estrutura da solução

Implementar uma arquitetura modular, evitando concentrar todo o fluxo em um único método ou no código da interface.

Componentes sugeridos:

| Componente | Responsabilidade |
|---|---|
| RecoveryOrchestrator | Controlar o fluxo e as decisões |
| RecoveryContext | Armazenar o estado da execução |
| PreflightValidator | Verificar ambiente, arquivos e ferramentas |
| DatabaseCopyManager | Criar e validar cópias |
| FirebirdToolRunner | Executar utilitários externos |
| CorruptionAnalyzer | Classificar erros e diagnósticos |
| BackupRestoreService | Controlar backup e restore |
| IndexRecoveryService | Tratar índices e constraints |
| PartialRecoveryService | Recuperar registros por tabela |
| DatabaseVerificationService | Validar o resultado |
| RecoveryManifestService | Registrar etapas e decisões |
| RecoveryReportService | Gerar relatórios |

Os nomes são sugestões. O desenvolvedor pode adaptá-los à estrutura existente.

O fluxo deverá utilizar uma máquina de estados explícita, com transições controladas e limite de tentativas por procedimento.

Não permitir repetição indefinida de comandos.

## 4. Organização dos arquivos

Cada execução deverá criar um diretório exclusivo:

```text
Recuperacao_GOURMET_20261104_140000/
│
├── manifest.json
├── original_hash.txt
│
├── work/
│   ├── copia_A.fdb
│   ├── copia_B.fdb
│   └── tentativas/
│
├── backup/
│   └── backup_recuperacao.fbk
│
├── staging/
│   └── banco_em_recuperacao.fdb
│
├── resultado/
│   └── GOURMET_RECUPERADO.fdb
│
├── logs/
│   ├── diagnostico.log
│   ├── backup.log
│   ├── mend.log
│   ├── restore.log
│   └── validacao.log
│
└── relatorios/
    ├── relatorio_final.html
    ├── tabelas.csv
    ├── perdas.csv
    ├── duplicados.csv
    └── registros_orfaos.csv
```

Todos os nomes devem ser exclusivos por tentativa, evitando que arquivos anteriores sejam sobrescritos.

O `manifest.json` deve permitir identificar quais etapas foram executadas, seus resultados e as decisões tomadas.

A retomada de uma execução deve validar hashes, existência dos artefatos e compatibilidade da versão antes de reutilizar qualquer resultado intermediário.

## 5. Fluxo principal de recuperação

O assistente deverá seguir uma ordem progressiva, começando pelos procedimentos menos invasivos.

### Etapa 0 — Pré-verificação

Antes da recuperação:

- Verificar existência e acesso ao arquivo.
- Identificar versão do Firebird e ODS.
- Identificar tamanho de página e características do banco.
- Verificar espaço disponível para todas as cópias, backups e restores.
- Validar versões e disponibilidade de `gfix`, `gbak`, `gstat` e `isql`.
- Identificar arquivos secundários, shadows e possíveis arquivos delta do nbackup.
- Verificar permissões do usuário e da conta do serviço Firebird.
- Validar caminhos com espaços e caracteres especiais.
- Verificar se o banco está em uso.
- Confirmar que a saída é diferente da origem.

Não assumir que quatro vezes o tamanho do banco será suficiente em todos os casos. O cálculo deve considerar o espaço disponível, as estimativas de crescimento e uma margem de segurança.

Arquivos secundários e deltas exigem tratamento específico. Se não houver uma rotina comprovadamente segura, bloquear a recuperação e orientar o usuário.

### Etapa 1 — Criar cópias de trabalho

Criar a cópia A a partir de uma origem consistente.

A cópia A será utilizada para diagnóstico e tentativa inicial de backup.

A cópia B será criada somente quando existir necessidade de reparo ou alteração de estrutura.

Antes de cada operação que modifica a cópia, preservar um checkpoint que permita retornar ao estado anterior.

### Etapa 2 — Diagnóstico inicial

Executar na cópia A:

```text
gfix -validate -full -no_update -ignore
```

Coletar:

- Erros de páginas.
- Erros de registros.
- Problemas em índices.
- Páginas órfãs.
- Fragmentos de registros.
- Problemas de BLOB.
- Inconsistências de transações.
- Falhas de leitura do catálogo.

Também verificar transações em limbo com `gfix -list`, quando possível.

Registrar a saída integral e classificar os erros conhecidos.

A classificação deverá distinguir corrupção física, corrupção lógica e falhas ambientais.

A ausência de erros no diagnóstico não garante a integridade dos dados.

### Etapa 3 — Backup conservador

Executar primeiro uma tentativa de backup na cópia A, sem realizar o mend.

Comando de referência:

```text
gbak -b -g -ig -v [origem] [backup.fbk]
```

A implementação deverá adicionar credenciais, captura de log e argumentos conforme a versão validada.

Ao concluir:

- Conferir o código de saída.
- Analisar mensagens de erro.
- Verificar existência e tamanho do arquivo gerado.
- Registrar tabelas mencionadas e contagens disponíveis.
- Não considerar um arquivo `.fbk` válido apenas porque ele existe.

Se o backup for concluído, seguir para o restore.

Se falhar, classificar a causa antes de escolher qualquer ação.

### Etapa 4 — Triagem da falha

Criar uma classificação centralizada de erros.

| Tipo de falha | Tratamento inicial |
|---|---|
| Erro físico de página | Avaliar mend em cópia |
| Índice independente corrompido | Avaliar desativação ou reconstrução |
| Índice de PK, UNIQUE ou FK | Tratamento específico da constraint |
| Registro ou BLOB ilegível | Avaliar recuperação parcial |
| Dados duplicados | Recuperar e registrar conflitos |
| Registro órfão | Recuperar e registrar inconsistência |
| Transação em limbo | Avaliação específica e autorização |
| Erro de permissão | Corrigir ambiente |
| Espaço insuficiente | Interromper sem reparar |
| Arquivo truncado | Avaliar viabilidade antes de tentar reparo |
| Metadados danificados | Tentar fonte íntegra de referência |

O log deve oferecer evidências para a decisão.

Uma tabela mencionada imediatamente antes da falha não deve ser considerada automaticamente a tabela que contém a página corrompida.

### Etapa 5 — Recuperação com mend

Executar somente após falha associada a corrupção física, em uma cópia separada e mediante confirmação.

Procedimento:

1. Criar a cópia B.
2. Registrar o diagnóstico anterior.
3. Executar `gfix -mend` com os parâmetros compatíveis.
4. Executar uma nova validação somente para diagnóstico.
5. Comparar os resultados disponíveis.
6. Tentar novamente o backup.
7. Registrar alterações, erros restantes e possíveis perdas.

O mend pode marcar registros como indisponíveis. Ele não garante preservar todos os dados.

Não atribuir automaticamente uma contagem exata de registros perdidos a partir do resultado do `gfix`.

As perdas devem ser classificadas como:

- Confirmadas.
- Estimadas.
- Não quantificáveis.

Repetir o mend apenas se houver justificativa técnica e uma cópia preservada para retorno.

### Etapa 6 — Tratamento de índices e constraints

Quando a falha envolver índices, consultar o catálogo para identificar o objeto e sua relação com constraints.

Distinguir:

- Índices independentes.
- Índices de PRIMARY KEY.
- Índices de UNIQUE.
- Índices de FOREIGN KEY.

Para índices independentes, avaliar desativação e reconstrução.

No Firebird 2.5, a desativação direta de índices que sustentam constraints não é permitida. Esses casos precisam de tratamento específico, sem tentar indiscriminadamente `ALTER INDEX ... INACTIVE`.

Antes de remover qualquer constraint:

1. Identificar sua definição completa.
2. Preservar a DDL.
3. Identificar dependências.
4. Registrar a operação.
5. Solicitar autorização.
6. Garantir um procedimento de recriação.
7. Validar o resultado após a alteração.

Utilizar DROP somente quando não existir alternativa mais segura e houver metadados suficientes para reconstrução.

### Etapa 7 — Restore controlado

Com um backup disponível, executar restore em banco novo.

Comando de referência:

```text
gbak -c -i -v [backup.fbk] [novo.fdb]
```

O parâmetro `-i` permite restaurar com os índices inativos, evitando falhas imediatas na construção dessas estruturas.

Se necessário, avaliar uma segunda tentativa com `-o` em outro banco novo, preservando as tabelas já restauradas por transações separadas.

O uso de `-n` ficará restrito a cenários de incompatibilidade com validações e constraints, mediante confirmação e testes específicos.

Não utilizar opções do Firebird 3.0 ou superior sem comprovar sua existência no Firebird 2.5.

Registrar:

- Resultado do restore.
- Tabelas processadas.
- Erros encontrados.
- Objetos não reconstruídos.
- Índices inativos.
- Constraints pendentes.

Nenhum restore parcial deverá ser classificado como completo.

### Etapa 8 — Recuperação avançada por tabela

Esta será a principal alternativa quando não for possível produzir um backup completo, mesmo após as tentativas anteriores.

O sistema deverá permitir reconstruir um banco a partir de metadados disponíveis e recuperar individualmente os dados acessíveis.

**8.1. Recuperação dos metadados**

Tentar obter os metadados, nesta ordem:

1. Backup de metadados da cópia.
2. Extração por `isql -x`.
3. Banco íntegro de referência, mediante seleção explícita do usuário.
4. Backup anterior compatível.

O banco de referência não deve ser considerado automaticamente idêntico ao banco danificado.

Comparar tabelas, campos, tipos, domínios, constraints e objetos dependentes antes de utilizar seus metadados.

Diferenças relevantes devem ser registradas e submetidas à aprovação.

**8.2. Recuperação individual das tabelas**

Criar um inventário das tabelas de usuário.

Para cada tabela:

- Identificar estrutura e campos.
- Identificar chave primária, índices e dependências.
- Testar se a tabela pode ser consultada.
- Avaliar estratégia de leitura.
- Recuperar registros acessíveis.
- Registrar falhas sem interromper automaticamente toda a recuperação.
- Preservar os dados já recuperados.

Uma tabela problemática não poderá impedir a tentativa de recuperação das demais.

**8.3. Recuperação por intervalos**

Quando existir uma chave utilizável, dividir a leitura em faixas.

Exemplo:

```text
Tabela: MOV_PEDIDOWEB

Faixa 1: CODIGO 1 até 1000
Faixa 2: CODIGO 1001 até 2000
Faixa 3: CODIGO 2001 até 3000
```

Se uma faixa falhar, subdividi-la progressivamente.

A estratégia deverá verificar o plano real de acesso e a integridade do índice utilizado.

**A divisão por chave não garante que o Firebird deixará de ler páginas danificadas.** Se a corrupção afetar páginas de dados ou índices necessários, a faixa poderá continuar ilegível.

Cada tentativa deve ter limite de profundidade, tempo e quantidade de operações.

**8.4. Estratégias alternativas**

Se a leitura por índice falhar:

1. Testar outra estratégia de acesso, sem alterar o original.
2. Avaliar leitura natural quando tecnicamente possível.
3. Tentar consultas com projeção reduzida de colunas.
4. Isolar BLOBs que impeçam a leitura de dados comuns.
5. Usar intervalos menores quando houver evidência de benefício.
6. Avaliar uma implementação opcional com provedor .NET do Firebird.
7. Registrar a tabela ou os registros como inacessíveis quando as alternativas falharem.

Um provedor .NET não garante contornar páginas fisicamente danificadas. Ele melhora o controle de consultas, transações e tratamento de erros, mas continua dependendo do mecanismo Firebird para acessar os dados.

Não desenvolver leitura física direta das páginas do arquivo FDB nesta fase. Isso exigiria outro mecanismo, com complexidade e riscos próprios.

**8.5. Recuperação de BLOBs**

Tentar preservar os campos comuns quando houver falha especificamente na leitura de BLOBs.

A substituição de conteúdo por `NULL` só poderá ocorrer quando a estrutura permitir e mediante autorização.

Registrar:

- Tabela.
- Chave ou identificador disponível.
- Campo afetado.
- Erro original.
- Conteúdo indisponível.
- Ação aplicada.

**8.6. Controle de gravação**

A carga no banco recuperado deverá utilizar transações controladas.

Não executar commit por registro indiscriminadamente, nem manter uma única transação para toda a recuperação.

O tamanho dos lotes deverá ser configurável.

Registrar checkpoints para permitir reprocessamento sem duplicar registros.

Uma etapa interrompida não poderá ser marcada como concluída.

### Etapa 9 — Reconstrução de índices e integridade

Após recuperar os dados, tentar reconstruir os índices e restabelecer as constraints.

Ordem sugerida:

1. Identificar definições e dependências.
2. Verificar duplicidades de chaves.
3. Reconstruir índices independentes.
4. Reativar índices de PK e UNIQUE.
5. Verificar relacionamentos entre tabelas.
6. Reativar índices associados a FKs.
7. Restaurar constraints e objetos removidos durante o processo.
8. Validar a integridade dos dados.

A ordem efetiva deverá respeitar as dependências dos objetos e os resultados dos testes.

Se houver duplicados, gerar relatório e preservar os registros. Não excluir automaticamente.

Se houver registros órfãos, identificar e apresentar os relacionamentos afetados.

A ativação de um índice de FK não substitui uma verificação explícita dos relacionamentos já existentes.

Se não for possível reconstruir um índice ou constraint, o banco continuará com pendência registrada.

### Etapa 10 — Validação do banco recuperado

A validação precisa combinar verificações físicas, estruturais e lógicas.

**10.1. Validação física**

Executar:

```text
gfix -validate -full -no_update
```

Analisar a saída e o código de retorno, não apenas a existência do arquivo.

**10.2. Validação de tabelas**

Para cada tabela acessível, executar `SELECT COUNT(*)` e registrar a contagem final.

Comparar, quando houver fontes confiáveis:

- Contagem de referência.
- Contagem na origem acessível.
- Contagem do processo de extração.
- Contagem carregada no destino.
- Contagem final.

Quando a origem não puder ser lida integralmente, declarar a impossibilidade de comprovar a ausência de perdas.

**10.3. Validação de metadados**

Comparar:

- Tabelas e colunas.
- Tipos e domínios.
- Constraints.
- Índices.
- Triggers.
- Procedures.
- Views.
- Generators.
- Dependências e permissões relevantes.

Normalizar diferenças irrelevantes de formatação da DDL antes do comparativo.

**10.4. Validação de integridade**

Verificar duplicados, registros órfãos, constraints pendentes, campos obrigatórios e possíveis inconsistências.

**10.5. Validação funcional**

Permitir cadastrar verificações SQL específicas para bases Gourmet, como totais de pedidos, itens, movimentos e outras informações de negócio.

A definição dessas consultas deverá ser validada com o esquema real de cada versão do banco.

Não considerar apenas a contagem de registros suficiente para validar o conteúdo.

### Etapa 11 — Resultado final

Classificar a recuperação de acordo com evidências:

**Completo validado:** backup e restore finalizados, verificações obrigatórias aprovadas e nenhuma divergência ou perda conhecida. A completude dos dados só poderá ser afirmada quando houver evidências suficientes.

**Recuperado com pendências:** banco restaurado, mas com índices, constraints, verificações ou diferenças não resolvidas.

**Parcial:** somente parte dos dados foi recuperada ou existem perdas confirmadas.

**Falhou:** não foi possível produzir um banco recuperado utilizável.

**Interrompido:** operação cancelada ou interrompida antes da conclusão.

O relatório deverá distinguir explicitamente integridade física, integridade estrutural e completude dos dados. Um banco fisicamente válido não é necessariamente completo.

## 6. Manifesto e rastreabilidade

Criar `manifest.json` com atualização atômica após cada etapa.

Informações mínimas:

- ID da execução.
- Versão do ConsultaSQL.
- Versões das ferramentas Firebird.
- Caminho e hash do original.
- Identificação dos arquivos de trabalho.
- Comandos executados, sem segredos.
- Início, término e duração.
- Código de saída.
- Logs associados.
- Decisões do usuário.
- Checkpoints.
- Contagens por tabela.
- Índices e constraints pendentes.
- Perdas confirmadas ou estimadas.
- Estado final.

Arquivos confidenciais, senhas e dados sensíveis não poderão aparecer em logs ou manifestos sem necessidade.

## 7. Segurança e execução dos comandos

Centralizar todos os processos externos em um `FirebirdToolRunner`.

Requisitos:

- Montagem segura dos argumentos.
- Suporte a caminhos com espaços e acentos.
- Captura de stdout e stderr.
- Leitura concorrente para evitar bloqueios.
- Cancelamento controlado da árvore de processos.
- Timeouts configuráveis.
- Registro de código de saída e duração.
- Tratamento de arquivos parciais.
- Proteção de credenciais.
- Compatibilidade com C# 5 e a versão real do .NET Framework utilizada.

A passagem de senhas deverá utilizar um mecanismo suportado pela ferramenta, preferencialmente arquivo temporário protegido, com exclusão garantida no encerramento.

Não expor senhas em argumentos, logs ou relatórios.

Para o `gbak -y`, utilizar sempre um caminho de log novo, pois a ferramenta pode falhar quando o arquivo já existe.

O cancelamento deverá impedir que arquivos incompletos sejam publicados como resultado.

Se um processo for encerrado à força, registrar esse fato e preservar os artefatos necessários para diagnóstico.

## 8. Interface do assistente

A interface deverá ser simples para quem utiliza a ferramenta, mesmo que internamente o processo seja complexo.

**Tela inicial**

- Selecionar banco.
- Selecionar pasta de destino.
- Informar credenciais.
- Verificar ambiente.
- Iniciar recuperação.

**Durante a recuperação**

Exibir as etapas:

1. Preparando ambiente.
2. Criando cópia segura.
3. Analisando o banco.
4. Tentando backup.
5. Executando reparo, se necessário.
6. Recuperando dados.
7. Reconstruindo estruturas.
8. Validando resultado.
9. Gerando relatório.

Cada etapa deverá indicar seu estado: pendente, executando, concluída, atenção ou falha.

Exibir progresso real quando mensurável. Quando não for possível calcular percentual, mostrar a atividade atual e os registros ou tabelas processados, sem inventar estimativas.

Disponibilizar detalhes técnicos em painel expansível.

**Tela final**

Exibir:

- Status da recuperação.
- Quantidade de tabelas processadas.
- Registros recuperados, quando mensuráveis.
- Tabelas com falhas.
- Perdas identificadas.
- Índices e constraints pendentes.
- Resultado das verificações.
- Caminho do banco gerado.

Permitir abrir o relatório e a pasta de saída.

## 9. Plano de testes obrigatório

Criar bancos de teste específicos para cada cenário.

| Caso | Cenário | Resultado esperado |
|---|---|---|
| CT001 | Banco íntegro | Recuperação e validação aprovadas |
| CT002 | Índice independente danificado | Erro identificado e tratamento seguro |
| CT003 | Duplicidade em chave única | Registros preservados e conflito relatado |
| CT004 | Registros órfãos | Órfãos identificados e relatados |
| CT005 | Página de dados corrompida | Tentativas controladas e perdas explicitadas |
| CT006 | BLOB corrompido | Tratamento conforme viabilidade e consentimento |
| CT007 | Arquivo truncado | Identificação e interrupção segura |
| CT008 | Cabeçalho danificado | Diagnóstico e bloqueio de ações inadequadas |
| CT009 | Metadados danificados | Avaliação de fonte de referência |
| CT010 | Banco em uso | Cópia insegura bloqueada |
| CT011 | Caminho com espaços e acentos | Execução correta |
| CT012 | Espaço insuficiente | Interrupção sem comprometer arquivos |
| CT013 | Cancelamento durante mend | Processo interrompido, original preservado |
| CT014 | Cancelamento durante restore | Resultado parcial não publicado |
| CT015 | Tabela grande com corrupção | Recuperação parcial testada |
| CT016 | Índice de PK danificado | Fluxo não depende de desativação proibida |
| CT017 | Falha em tabela intermediária | Demais tabelas são tentadas |
| CT018 | Senha com caracteres especiais | Credenciais tratadas corretamente |
| CT019 | Falha na escrita do manifesto | Recuperação interrompida ou estado reconciliado |
| CT020 | Retomada de execução | Sem duplicação de registros |
| CT021 | Banco com arquivos secundários | Bloqueio ou tratamento comprovadamente seguro |
| CT022 | Banco de referência incompatível | Diferenças detectadas antes da utilização |

O corpus deverá conter dados conhecidos para permitir comparar registros antes e depois da recuperação.

Os testes de corrupção deverão usar exclusivamente cópias descartáveis.

Aprovação não significa necessariamente recuperar todos os registros de uma página destruída. Significa preservar o original, recuperar corretamente os dados acessíveis, evitar perdas silenciosas e apresentar um resultado verificável.

## 10. Fases de desenvolvimento

A implementação deverá ocorrer progressivamente. Uma fase só poderá ser considerada concluída após testes e evidências.

### Fase 1 — Base segura e infraestrutura

Implementar:

- Pré-verificação.
- Hash e cópias.
- ToolRunner.
- Proteção de credenciais.
- Manifesto.
- Logs.
- Cancelamento.
- Máquina de estados.
- Interface inicial.

**Aceite:** banco original preservado, cópia consistente, processos controlados, falhas registradas e cancelamento funcional.

### Fase 2 — Backup, restore e validação

Implementar:

- Backup conservador.
- Restore com índices inativos.
- Identificação e reativação de índices.
- Verificação física.
- Contagens por tabela.
- Comparação de metadados.
- Relatório de pendências.

**Aceite:** recuperação de banco íntegro e dos casos controlados de duplicidade e integridade referencial, sem omissões silenciosas.

### Fase 3 — Diagnóstico avançado e reparo

Implementar:

- Classificador de erros.
- Mend controlado.
- Comparação de diagnósticos.
- Tratamento específico de índices e constraints.
- Utilização controlada de banco de referência.

**Aceite:** cenários de corrupção física e de índices tratados com segurança e relatórios claros.

### Fase 4 — Recuperação parcial

Implementar:

- Recuperação individual por tabela.
- Leitura por intervalos.
- Estratégias alternativas de acesso.
- Tratamento de BLOB.
- Controle de transações e checkpoints.
- Registro detalhado de perdas.
- Retomada de execução.

**Aceite:** recuperação do máximo de dados acessíveis no corpus, sem interromper todas as tabelas por uma única falha e sem duplicar registros em retomadas.

### Fase 5 — Validação final e acabamento

Implementar:

- Relatórios completos.
- Classificação do resultado.
- Melhorias de interface.
- Verificações funcionais configuráveis.
- Documentação.
- Revisão de desempenho e segurança.

**Aceite:** fluxo completo testado, documentação atualizada e tratamento adequado dos cenários de falha.

## 11. Requisitos específicos para o caso GOURMET.FDB

O histórico analisado apresenta:

- Cinco erros de página identificados na validação.
- Falha do backup na página 2200.
- Divergência entre tipo esperado 5 e encontrado 12.
- Falha na reconstrução da chave primária de `CONFIG_MESA`.
- Backup ainda impedido após tentativa de reparo.

Esse cenário deverá integrar o planejamento de testes, mas não pode ser tratado como resultado comprovado do novo assistente.

Não assumir que a tabela `CONFIG_REDE_CONCILIADOR` contém a página danificada somente por aparecer antes do erro no log.

Se houver autorização para usar o `GOURMET.FDB` real em uma avaliação, o programa deverá trabalhar exclusivamente em cópias e preservar os hashes e logs de todas as tentativas.

O banco de referência de 19/10/2026 poderá ser considerado para análise de metadados, desde que sua compatibilidade estrutural seja verificada. Não utilizar automaticamente seus dados para substituir os do banco corrompido.

O objetivo desse teste será identificar se o novo fluxo consegue recuperar dados que o processo anterior não conseguiu, com evidências claras do que foi preservado.

## 12. Critérios gerais de aceite

A implementação só poderá ser considerada aprovada quando:

1. O banco original permanecer inalterado, comprovado por hash quando tecnicamente verificável.
2. Nenhuma operação destrutiva ocorrer sem confirmação.
3. Os comandos forem compatíveis com Firebird 2.5.
4. O programa conseguir diferenciar falha ambiental de corrupção.
5. Um erro em determinada tabela não interromper automaticamente a tentativa das demais na recuperação parcial.
6. O restore não for declarado bem-sucedido apenas por gerar um arquivo FDB.
7. Os índices e constraints pendentes forem identificados.
8. As perdas conhecidas forem registradas e as perdas não quantificáveis forem explicitadas.
9. O relatório for gerado também em casos de falha ou cancelamento.
10. As credenciais não aparecerem nos logs.
11. A retomada não duplicar registros.
12. Todos os cenários obrigatórios do corpus forem testados.
13. O README refletir as funcionalidades realmente implementadas.
14. A compilação local e o workflow de build passarem.
15. A publicação de cada versão no GitHub depender de autorização explícita.

## 13. Orientações finais ao desenvolvedor

Antes de começar cada fase, revisar o código existente e aproveitar as implementações já funcionais. Não reescrever o assistente inteiro sem necessidade.

Atualizar o README antes das alterações, registrando o funcionamento atual e as limitações conhecidas. Após implementar e testar uma fase, atualizar novamente a documentação com o que realmente ficou disponível.

Sempre separar:

- Funcionalidade implementada.
- Funcionalidade compilada.
- Funcionalidade testada.
- Funcionalidade validada com corrupção real ou simulada.

Uma compilação bem-sucedida não comprova a eficácia da recuperação.

Não executar uma nova tentativa no banco real sem autorização.

Entregar cada fase com resumo das alterações, arquivos modificados, testes executados, resultados, limitações e evidências.

**A prioridade é recuperar a maior quantidade possível de dados sem comprometer o original e sem apresentar um banco incompleto como se estivesse íntegro.**

A ferramenta deve ser capaz de informar claramente tanto seus sucessos quanto seus limites. O resultado precisa ser tecnicamente verificável, e não apenas um arquivo FDB que consegue abrir.