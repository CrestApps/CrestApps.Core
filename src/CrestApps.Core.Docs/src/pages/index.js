import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import HomepageFeatures from '@site/src/components/HomepageFeatures';

import Heading from '@theme/Heading';
import styles from './index.module.css';

function HomepageHeader() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <header className={clsx('hero hero--primary', styles.heroBanner)}>
      <div className={clsx('container', styles.heroContent)}>
        <div className={styles.heroText}>
          <Heading as="h1" className="hero__title">
            Build AI chat, agents, and automation into your .NET app
          </Heading>
          <p className="hero__subtitle">{siteConfig.tagline}</p>
          <p className={styles.heroLead}>
            Start with connections, deployments, and Chat Interactions, then grow into
            documents, RAG, MCP, A2A, reporting, and custom AI tooling without
            reworking your architecture.
          </p>
          <div className={styles.buttons}>
            <Link
              className="button button--secondary button--lg"
              to="/docs/getting-started">
              Quick Start Guide
            </Link>
            <Link
              className="button button--outline button--secondary button--lg"
              to="/docs/core/getting-started-aspnet">
              ASP.NET Core Setup
            </Link>
          </div>
        </div>
        <div className={styles.heroVisual}>
          <img
            className={styles.heroImage}
            src="/img/docs/crestapps.core-dotnet-project.png"
            alt="CrestApps.Core feature overview"
          />
        </div>
      </div>
    </header>
  );
}

export default function Home() {
  return (
    <Layout
      title="Documentation"
      description="CrestApps.Core is the composable AI management and application framework for .NET with orchestration, chat, RAG, agents, MCP, A2A, reporting, and extensibility.">
      <HomepageHeader />
      <main>
        <HomepageFeatures />
      </main>
    </Layout>
  );
}
