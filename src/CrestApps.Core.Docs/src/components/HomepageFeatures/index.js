import clsx from 'clsx';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

const FeatureList = [
    {
        title: 'AI management and orchestration',
        emoji: '🤖',
        description: (
            <>
                Manage AI profiles, connections, deployments, tools, templates, data sources,
                and orchestration in one reusable .NET framework.
            </>
        ),
    },
    {
        title: 'Chat, RAG, and business workflows',
        emoji: '💬',
        description: (
            <>
                Build chat experiences with documents, memory, metrics, reporting,
                extraction, lead collection, and live-agent handoff.
            </>
        ),
    },
    {
        title: 'Protocols, agents, and extensibility',
        emoji: '🧩',
        description: (
            <>
                Support MCP, A2A, AI agents, Copilot orchestration, and custom AI functions
                while keeping every layer customizable from code.
            </>
        ),
    },
];

function Feature({ emoji, title, description }) {
    return (
        <div className={clsx('col col--4')}>
            <div className="text--center" style={{ fontSize: '3rem' }}>
                {emoji}
            </div>
            <div className="text--center padding-horiz--md">
                <Heading as="h3">{title}</Heading>
                <p>{description}</p>
            </div>
        </div>
    );
}

export default function HomepageFeatures() {
    return (
        <section className={styles.features}>
            <div className="container">
                <div className="row">
                    {FeatureList.map((props, idx) => (
                        <Feature key={idx} {...props} />
                    ))}
                </div>
            </div>
        </section>
    );
}
