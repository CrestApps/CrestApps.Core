---
Title: Data Extraction Prompt
Description: User prompt containing extraction targets, current state, and the latest chat turn
IsListable: false
Category: Extraction
Parameters:
  - fields: array of field objects with Name, Description, AllowMultipleValues, IsUpdatable, FieldType, and AllowedValues
  - currentState: array of extracted field objects with Name and Values
  - lastAssistantMessage: the last assistant message before the latest user message
  - lastUserMessage: the latest user message to extract from
---

Extract the following fields from the user's latest message.

Fields to extract:
{% for field in fields %}
- {{ field.Name }}{% if field.Description %}: {{ field.Description }}{% endif %} (multiple: {{ field.AllowMultipleValues | downcase }}, updatable: {{ field.IsUpdatable | downcase }}){% if field.FieldType and field.FieldType != "text" %} Type: {% case field.FieldType %}{% when "number" %}a number, digits only{% when "boolean" %}true or false{% when "date" %}a date as yyyy-MM-dd{% when "enum" %}exactly one of [{% for option in field.AllowedValues %}{% if forloop.index0 > 0 %}, {% endif %}{{ option }}{% endfor %}]{% when "list" %}every item the user gives, one value per item{% if field.AllowedValues.size > 0 %}, each exactly one of [{% for option in field.AllowedValues %}{% if forloop.index0 > 0 %}, {% endif %}{{ option }}{% endfor %}]{% endif %}{% endcase %}. Leave the field out if the user's message does not give a value of that type.{% endif %}
{% endfor %}

{% if currentState and currentState.size > 0 %}
Current extracted state:
{% for field in currentState %}
- {{ field.Name }}: [{% for value in field.Values %}{% if forloop.index0 > 0 %}, {% endif %}{{ value }}{% endfor %}]
{% endfor %}
{% endif %}

{% if lastAssistantMessage %}
Last assistant message: {{ lastAssistantMessage }}
{% endif %}

Latest user message: {{ lastUserMessage }}
