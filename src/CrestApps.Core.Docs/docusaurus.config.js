// @ts-check

import { themes as prismThemes } from 'prism-react-renderer';

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'CrestApps Core',
  tagline: 'Composable AI management and application framework for .NET',
  favicon: 'img/favicon.ico',
  titleDelimiter: '|',

  future: {
    v4: true,
  },

  url: 'https://core.crestapps.com',
  baseUrl: '/',

  organizationName: 'CrestApps',
  projectName: 'CrestApps.Core',

  onBrokenLinks: 'warn',

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  themes: [
    [
      '@easyops-cn/docusaurus-search-local',
      ({
        hashed: true,
        language: ['en'],
        highlightSearchTermsOnTargetPage: true,
        explicitSearchResultPath: true,
      }),
    ],
  ],

  presets: [
    [
      'classic',
      ({
        docs: {
          sidebarPath: './sidebars.js',
          editUrl: 'https://github.com/CrestApps/CrestApps.Core/tree/main/src/CrestApps.Core.Docs/',
          lastVersion: 'current',
          versions: {
            current: {
              label: 'Latest',
              path: '',
            },
          },
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      }),
    ],
  ],

  themeConfig:
    ({
      image: 'img/logo.png',
      colorMode: {
        defaultMode: 'light',
        respectPrefersColorScheme: true,
      },
      navbar: {
        title: 'CrestApps Core',
        logo: {
          alt: 'CrestApps Logo',
          src: 'img/logo.svg',
        },
        items: [
          {
            type: 'docSidebar',
            sidebarId: 'docsSidebar',
            position: 'left',
            label: 'Docs',
          },
          {
            type: 'docsVersionDropdown',
            position: 'right',
            dropdownActiveClassDisabled: true,
          },
          {
            href: 'https://github.com/CrestApps/CrestApps.Core',
            label: 'GitHub',
            position: 'right',
          },
        ],
      },
      footer: {
        style: 'dark',
        links: [
          {
            title: 'Documentation',
            items: [
              { label: 'Getting Started', to: '/docs/getting-started' },
              { label: 'Core Overview', to: '/docs/core' },
              { label: 'Sample Projects', to: '/docs/core/sample-projects' },
            ],
          },
          {
            title: 'Community',
            items: [
              { label: 'Issues', href: 'https://github.com/CrestApps/CrestApps.Core/issues' },
            ],
          },
          {
            title: 'More',
            items: [
              { label: 'GitHub', href: 'https://github.com/CrestApps/CrestApps.Core' },
              { label: 'Cloudsmith Preview Feed', href: 'https://cloudsmith.io/~crestapps/repos/crestapps-core' },
              { label: 'CrestApps', href: 'https://crestapps.com' },
            ],
          },
        ],
        copyright: `Copyright © ${new Date().getFullYear()} CrestApps.Core.`,
      },
      prism: {
        theme: prismThemes.github,
        darkTheme: prismThemes.dracula,
        additionalLanguages: ['csharp', 'json', 'bash'],
      },
    }),
};

export default config;
