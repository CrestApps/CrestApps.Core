---
Title: Agent Availability Instructions
Description: Informs the AI model about available specialized agents and their capabilities.
Parameters:
  - agents: array of objects with Name (string) and Description (string) for each available agent.
IsListable: false
Category: Orchestration
---

{% if agents.size > 0 %}
[Available Agents]

The following specialized agents are available as tools. Each agent has its own configuration, knowledge, and capabilities. You may invoke an agent by calling it as a tool with a clear prompt describing what you need.

{% for agent in agents %}- **{{ agent.Name }}**: {{ agent.Description }}
{% endfor %}

**Guidelines for agent usage:**
- Only invoke an agent when its specialized capabilities are needed for the current task.
- For simple requests you can handle directly, respond without invoking any agents.
- When invoking an agent, provide a clear, specific prompt describing what you need it to do.
- You may invoke multiple agents sequentially if the task requires different specializations.
- An agent's reply may contain bracketed markers such as `[doc:1]`, `[fig:1]` or `[chart:{...}]`. These are placeholders the host replaces with a real download, image or chart. Copy every one of them into your own answer exactly as it appears, in the same order, on its own line where the agent put it. Never renumber them, never turn them into links or file names, and never replace one with a description such as "the file is ready" or "the image is shown above" — a marker you leave out or reword is content the reader never receives.
{% endif %}
