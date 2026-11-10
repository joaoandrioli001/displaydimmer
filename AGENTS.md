# Agentes do projeto

Este projeto usa um sistema de agentes. Toda solicitação do usuário começa pelo Gestor.

Antes de qualquer coisa, leia o arquivo do agente que você é ou que vai acionar.

## Regra obrigatória

Nenhum arquivo de código pode ser criado, alterado ou apagado sem um ticket em `tickets/`. Se não existir ticket para a solicitação, pare, assuma o papel do Gestor e crie o ticket primeiro.

## Agentes

| Agente | Arquivo | Função |
|---|---|---|
| Gestor | .github/agents/gestor.agent.md | Conversa com o usuário, planeja, cria tickets e delega tarefas |
| Programador | .github/agents/programador.agent.md | Implementa, altera e remove código |
| Programador Sênior | .github/agents/programador-senior.agent.md | Resolve tarefas complexas: arquitetura, bugs difíceis, integrações delicadas |
| Testador | .github/agents/testador.agent.md | Valida se o que foi feito funciona |
| Designer | .github/agents/designer.agent.md | Define e ajusta interface e visual |
| Documentador | .github/agents/documentador.agent.md | Registra e atualiza a documentação na pasta Docs |

## Como funciona

1. O usuário faz uma solicitação e o Gestor assume.
2. O Gestor cria um ticket numerado na pasta tickets, define função e escopo e monta o plano.
3. O Gestor envia uma tarefa por vez ao agente responsável.
4. Ao terminar, o agente informa o Gestor e pede que ele marque a tarefa como concluída no ticket.
5. O Gestor delega a próxima tarefa e, no fim, confere os requisitos e informa o resultado ao usuário.

## Estrutura de pastas

```
AGENTS.md
.github/agents/   arquivos dos agentes (*.agent.md)
tickets/       um arquivo por solicitação (ticket-1.md, ticket-2.md, ...)
Docs/
```

## Regras para todos os agentes

- Trabalhar somente dentro do escopo definido no ticket e nunca mexer no que está em "Fora do escopo".
- Fazer apenas o que a tarefa pede.
- Só o Gestor cria tickets e marca tarefas como concluídas.
- Se faltar informação, perguntar ao Gestor em vez de assumir.
- Tarefas pequenas não precisam de testes.
- Escrever sempre em português do Brasil (pt-BR).
