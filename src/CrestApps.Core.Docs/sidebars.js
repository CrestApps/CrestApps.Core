// @ts-check

/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  docsSidebar: [
    'intro',
    'getting-started',
    {
      type: 'category',
      label: 'Core',
      collapsed: false,
      items: [
        'core/index',
        'core/architecture',
        'core/getting-started-aspnet',
        'core/use-cases',
        'core/core-services',
        'core/ai-core',
        'core/orchestration',
        'core/chat',
        'core/document-processing',
        'core/ai-templates',
        'core/tools',
        'core/agents',
        'core/copilot',
        'core/response-handlers',
        'core/context-builders',
        'core/signalr',
        'core/data-storage',
        'core/ai-documents',
        'core/ai-memory',
        'core/interfaces',
        {
          type: 'category',
          label: 'AI Providers',
          items: [
            'providers/index',
            'providers/openai',
            'providers/azure-openai',
            'providers/ollama',
            'providers/azure-ai-inference',
          ],
        },
        {
          type: 'category',
          label: 'Data Sources',
          items: [
            'data-sources/index',
            'data-sources/elasticsearch',
            'data-sources/azure-ai',
          ],
        },
        {
          type: 'category',
          label: 'Model Context Protocol (MCP)',
          items: [
            'mcp/index',
            'mcp/client',
            'mcp/server',
            'mcp/resource-types',
          ],
        },
        {
          type: 'category',
          label: 'Agent-to-Agent Protocol (A2A)',
          items: [
            'a2a/index',
            'a2a/client',
            'a2a/host',
          ],
        },
        'core/mvc-example',
      ],
    },
    {
      type: 'category',
      label: 'Changelog',
      items: [
        'changelog/index',
        'changelog/v1.0.0',
      ],
    },
  ],
};

export default sidebars;
