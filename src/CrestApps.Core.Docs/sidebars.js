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
                'core/architecture',
                'core/core-services',
                'core/extensible-entity',
                'core/getting-started-aspnet',
                'core/index',
                'core/interfaces',
                'core/mvc-example',
                'core/blazor-example',
            ],
        },
        {
            type: 'category',
            label: 'Features',
            collapsed: false,
            items: [
                {
                    type: 'category',
                    label: 'Agent-to-Agent Protocol (A2A)',
                    items: [
                        'a2a/index',
                        'a2a/client',
                        'a2a/host',
                    ],
                },
                'core/agents',
                'core/ai-core',
                'core/ai-documents',
                'core/ai-memory',
                'core/ai-templates',
                'core/chat',
                'core/context-builders',
                'core/copilot',
                {
                    type: 'category',
                    label: 'Data Sources',
                    items: [
                        'data-sources/index',
                        'data-sources/azure-ai',
                        'data-sources/elasticsearch',
                    ],
                },
                'core/data-storage',
                'core/document-processing',
                {
                    type: 'category',
                    label: 'Model Context Protocol (MCP)',
                    items: [
                        'mcp/index',
                        'mcp/client',
                        'mcp/resource-types',
                        'mcp/server',
                    ],
                },
                'core/orchestration',
                {
                    type: 'category',
                    label: 'AI Providers',
                    items: [
                        'providers/index',
                        'providers/azure-ai-inference',
                        'providers/azure-openai',
                        'providers/ollama',
                        'providers/openai',
                    ],
                },
                'core/response-handlers',
                'core/signalr',
                'core/tools',
                'core/use-cases',
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
