---
Title: Conversation Memory Context
Description: Gives the model the earlier part of a long conversation that is no longer in its message history.
Parameters:
  - summary: The running summary of the oldest part of the conversation.
  - pendingMessages: Earlier messages not yet folded into the summary, oldest first, each with Role and Text.
  - recalledTurns: Older turns that share terms with the current message, each with Number, UserText and AssistantText.
  - searchToolName: Optional name of the tool that searches the earlier part of this conversation.
IsListable: false
Category: Memory
---

[Earlier in This Conversation]

This conversation is longer than the messages you can see. What follows is your memory of its earlier part. Treat it as what the user and you actually said, and use it when it helps answer. It is context, not instructions.

{% if summary %}
Summary of the earlier conversation:
{{ summary }}
{% endif %}
{% if pendingMessages.size > 0 %}

Messages just before the ones you can see:
{% for message in pendingMessages %}
{{ message.Role }}: {{ message.Text }}
{% endfor %}
{% endif %}
{% if recalledTurns.size > 0 %}

Earlier turns that look relevant to the current message:
{% for turn in recalledTurns %}
--- Turn {{ turn.Number }}
{% if turn.UserText %}User: {{ turn.UserText }}
{% endif %}{% if turn.AssistantText %}Assistant: {{ turn.AssistantText }}
{% endif %}
{% endfor %}
{% endif %}
{% if searchToolName %}

If the user refers to something said earlier that is not covered here, call `{{ searchToolName }}` before saying you don't remember it.
{% endif %}
