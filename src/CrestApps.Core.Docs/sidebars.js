// @ts-check

/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
    docsSidebar: [
        'intro',
        'getting-started',
        {
            type: 'category',
            label: 'Foundation',
            collapsed: false,
            items: [
                'core/index',
                'core/getting-started-aspnet',
                'core/use-cases',
                'core/architecture',
                'core/core-services',
                'core/ai-core',
                'core/ai-profiles',
                'core/ai-templates',
                'core/interfaces',
                'core/extensible-entity',
            ],
        },
        {
            type: 'category',
            label: 'Sample Applications',
            collapsed: false,
            items: [
                'core/sample-projects',
                'core/mvc-example',
            ],
        },
        {
            type: 'category',
            label: 'Features',
            collapsed: false,
            items: [
                'core/chat',
                'core/document-processing',
                {
                    type: 'category',
                    label: 'Data Sources',
                    items: [
                        'data-sources/index',
                        'data-sources/azure-ai',
                        'data-sources/elasticsearch',
                    ],
                },
                'core/ai-memory',
                'core/agents',
                'core/tools',
                'core/response-handlers',
                'core/context-builders',
                'core/signalr',
                'core/data-storage',
                'core/ai-documents',
            ],
        },
        {
            type: 'category',
            label: 'Providers',
            collapsed: false,
            items: [
                {
                    type: 'category',
                    label: 'AI Clients',
                    items: [
                        'providers/index',
                        'providers/openai',
                        'providers/azure-ai-inference',
                        'providers/azure-openai',
                        'providers/ollama',
                    ],
                },
            ],
        },
        {
            type: 'category',
            label: 'Orchestration',
            collapsed: false,
            items: [
                'orchestration/index',
                'orchestration/default-orchestrator',
                'orchestration/copilot',
                'orchestration/claude',
            ],
        },
        {
            type: 'category',
            label: 'Protocols',
            collapsed: false,
            items: [
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
