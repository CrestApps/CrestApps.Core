---
Title: Document Availability Instructions
Description: Instructs the AI about uploaded documents and available tools.
Parameters:
  - tools: array of AIToolDefinitionEntry objects for document processing tools available.
  - knowledgeBaseDocuments: array of profile-level ChatDocumentInfo objects that are hidden background knowledge.
  - userSuppliedDocuments: array of non-image session/user-level ChatDocumentInfo objects that are user-visible uploads/attachments.
  - visionUserSuppliedDocuments: array of supported image session/user-level ChatDocumentInfo objects that are attached to the current user message as multimodal inputs.
IsListable: false
Category: Documents
---

[Available Documents, attachments or files]

{% assign hasDocumentTools = tools.size > 0 %}
{% assign hasVisionUserSuppliedDocuments = visionUserSuppliedDocuments.size > 0 %}
{% assign hasUserSuppliedDocuments = userSuppliedDocuments.size > 0 %}
{% assign hasKnowledgeBaseDocuments = knowledgeBaseDocuments.size > 0 %}

{% if hasVisionUserSuppliedDocuments %}
The user has uploaded the following image attachments as supplementary context.
These supported image attachments are already attached to the current user message as multimodal inputs.
When the user asks what is shown in one of these images, inspect the image directly and answer from its visual content.
Do not say that you cannot view images or ask the user to upload the image again unless the image input is actually unavailable.

### Available image attachments:
{% for doc in visionUserSuppliedDocuments %}
- {{ doc.DocumentId }}: "{{ doc.FileName }}" ({{ doc.ContentType | default: "unknown" }}, {{ doc.FileSize }} bytes)
{% endfor %}
{% endif %}

{% if hasUserSuppliedDocuments %}
{% if hasDocumentTools %}
The user has uploaded the following documents as supplementary context.
Use the document tools before answering: prefer semantic search for targeted lookups, and read a full document when the task requires whole-file context such as summarizing, reviewing, rewriting, translating, or extracting complete information from an uploaded file.
{% if isInScope %}
Answer only from the uploaded documents and retrieved document context.
If the documents do not contain the answer, clearly say that the answer is not available in the uploaded documents.
Do not use your general knowledge to fill gaps.
{% else %}
If the documents contain relevant information, base your answer on that content.
If the documents do not contain relevant information, use your general knowledge to answer instead.
Do not refuse to answer simply because the documents lack the requested information.
{% endif %}

{% if hasDocumentTools %}
### Available document tools:
{% endif %}
{% for tool in tools %}
- {{ tool.Name }}: {{ tool.Description }}
{% endfor %}
{% else %}
The user has uploaded the following documents as supplementary context.
{% if isInScope %}
Answer only from the uploaded documents.
If the uploaded documents do not contain the answer, say so instead of using general knowledge.
{% endif %}
{% endif %}

{% if hasUserSuppliedDocuments %}
### Available documents:
{% for doc in userSuppliedDocuments %}
- {{ doc.DocumentId }}: "{{ doc.FileName }}" ({{ doc.ContentType | default: "unknown" }}, {{ doc.FileSize }} bytes)
{% endfor %}
{% endif %}

{% if hasKnowledgeBaseDocuments %}
Background knowledge is available for this profile.
{% if hasDocumentTools %}
Search the profile knowledge documents first using the available document tools before answering.
### Available document tools:
{% for tool in tools %}
- {{ tool.Name }}: {{ tool.Description }}
{% endfor %}
Use the available document tools and background context to answer accurately.
{% else %}
Use the available background context to answer accurately.
{% endif %}
{% if isInScope %}
Stay grounded in the retrieved profile knowledge and document context only.
If the available knowledge does not contain the answer, say that you do not have enough retrieved information.
{% endif %}
Do not mention knowledge-base documents, files, uploads, or attachments unless the user explicitly uploaded files in this session.
{% endif %}
