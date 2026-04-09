import clsx from 'clsx';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

const FeatureList = [
  {
    title: 'Launch AI chat and agent experiences',
    emoji: '🚀',
    description: (
      <>
        Start with Chat Interactions as a playground-style UI, then grow into reusable
        AI agents, orchestration flows, and production chat experiences.
      </>
    ),
  },
  {
    title: 'Control models, connections, and runtime behavior',
    emoji: '🎛️',
    description: (
      <>
        Configure providers, credentials, deployments, prompts, and reusable agent
        profiles without scattering AI setup across the whole app.
      </>
    ),
  },
  {
    title: 'Connect documents, tools, MCP, and A2A',
    emoji: '🧩',
    description: (
      <>
        Add RAG, document workflows, custom AI tools, Model Context Protocol, and
        agent-to-agent integration on the same composable service foundation.
      </>
    ),
  },
];

function Feature({ emoji, title, description }) {
  return (
    <div className={clsx('col col--4')}>
      <div className={styles.featureCard}>
        <div className={styles.featureEmoji}>{emoji}</div>
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
