import { defineConfig } from "vitepress";

export default defineConfig({
  title: "OrbitMesh",
  description: "A stateless, self-hosted hub for edge devices and home automation packages.",
  // GitHub Pages project site (no custom domain/CNAME): served at /orbitmesh/, not the root -
  // every generated asset/link needs this prefix or the deployed site 404s on its own JS/CSS.
  base: "/orbitmesh/",
  cleanUrls: true,
  lastUpdated: true,

  locales: {
    root: {
      label: "English",
      lang: "en",
      description: "A stateless, self-hosted hub for edge devices and home automation packages.",
      themeConfig: {
        nav: [
          { text: "Guide", link: "/guide/" },
          { text: "Packages", link: "/packages" }
        ],
        sidebar: {
          "/guide/": [
            { text: "What is OrbitMesh?", link: "/guide/" },
            {
              text: "Installation",
              collapsed: false,
              items: [
                { text: "Quick install", link: "/guide/installation/" },
                { text: "Manual build", link: "/guide/installation/manual-build" },
                { text: "Running Server & Edge", link: "/guide/installation/running" },
                { text: "Background service", link: "/guide/installation/background-service" }
              ]
            },
            {
              text: "Architecture",
              collapsed: false,
              items: [
                { text: "Overview", link: "/guide/architecture/" },
                { text: "Access control", link: "/guide/architecture/access-control" },
                { text: "Variables", link: "/guide/architecture/variables" },
                { text: "Package repository & distribution", link: "/guide/architecture/packages" },
                { text: "Hosting other sites", link: "/guide/architecture/external-sites" },
                { text: "Recovery & updates", link: "/guide/architecture/recovery-updates" }
              ]
            },
            {
              text: "Building a package (SDK)",
              collapsed: false,
              items: [
                { text: "Getting started", link: "/guide/sdk/" },
                { text: "Settings", link: "/guide/sdk/settings" },
                { text: "Telemetry", link: "/guide/sdk/telemetry" },
                { text: "Messages (RPC)", link: "/guide/sdk/messages" },
                { text: "Logging", link: "/guide/sdk/logging" },
                { text: "PackageInfo.xml", link: "/guide/sdk/manifest" },
                { text: "Packaging and distribution", link: "/guide/sdk/packaging" }
              ]
            }
          ]
        },
        editLink: {
          pattern: "https://github.com/orbitmesh-project/orbitmesh/edit/main/src/orbitmesh-site/:path"
        }
      }
    },
    fr: {
      label: "Français",
      lang: "fr",
      description: "Un hub stateless et auto-hébergé pour vos appareils connectés et vos packages domotiques.",
      themeConfig: {
        nav: [
          { text: "Guide", link: "/fr/guide/" },
          { text: "Packages", link: "/fr/packages" }
        ],
        sidebar: {
          "/fr/guide/": [
            { text: "Qu'est-ce qu'OrbitMesh ?", link: "/fr/guide/" },
            {
              text: "Installation",
              collapsed: false,
              items: [
                { text: "Installation rapide", link: "/fr/guide/installation/" },
                { text: "Build manuel", link: "/fr/guide/installation/manual-build" },
                { text: "Lancer Server & Edge", link: "/fr/guide/installation/running" },
                { text: "Service d'arrière-plan", link: "/fr/guide/installation/background-service" }
              ]
            },
            {
              text: "Architecture",
              collapsed: false,
              items: [
                { text: "Vue d'ensemble", link: "/fr/guide/architecture/" },
                { text: "Contrôle d'accès", link: "/fr/guide/architecture/access-control" },
                { text: "Variables", link: "/fr/guide/architecture/variables" },
                { text: "Dépôt de packages et distribution", link: "/fr/guide/architecture/packages" },
                { text: "Héberger d'autres sites", link: "/fr/guide/architecture/external-sites" },
                { text: "Récupération et mises à jour", link: "/fr/guide/architecture/recovery-updates" }
              ]
            },
            {
              text: "Créer un package (SDK)",
              collapsed: false,
              items: [
                { text: "Démarrage", link: "/fr/guide/sdk/" },
                { text: "Settings", link: "/fr/guide/sdk/settings" },
                { text: "Télémétrie", link: "/fr/guide/sdk/telemetry" },
                { text: "Messages (RPC)", link: "/fr/guide/sdk/messages" },
                { text: "Logs", link: "/fr/guide/sdk/logging" },
                { text: "PackageInfo.xml", link: "/fr/guide/sdk/manifest" },
                { text: "Packaging et distribution", link: "/fr/guide/sdk/packaging" }
              ]
            }
          ]
        },
        editLink: {
          pattern: "https://github.com/orbitmesh-project/orbitmesh/edit/main/src/orbitmesh-site/:path"
        },
        outline: { label: "Sur cette page" },
        docFooter: { prev: "Précédent", next: "Suivant" },
        returnToTopLabel: "Retour en haut",
        sidebarMenuLabel: "Menu",
        darkModeSwitchLabel: "Apparence",
        lastUpdatedText: "Dernière mise à jour"
      }
    }
  },

  themeConfig: {
    socialLinks: [
      { icon: "github", link: "https://github.com/orbitmesh-project/orbitmesh" }
    ],
    search: { provider: "local" }
  }
});
