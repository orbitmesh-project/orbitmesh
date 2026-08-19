import { store } from "./store.js";
import Login from "../Pages/Login.js";
import Home from "../Pages/Home.js";
import ConsoleLog from "../Pages/ConsoleLog.js";
import Edges from "../Pages/Edges.js";
import Packages from "../Pages/Packages.js";
import Variables from "../Pages/Variables.js";
import PackageRepository from "../Pages/PackageRepository.js";
import TelemetryItems from "../Pages/TelemetryItems.js";
import MessageHandlers from "../Pages/MessageHandlers.js";
import Credentials from "../Pages/Credentials.js";
import ConfigurationEditor from "../Pages/ConfigurationEditor.js";

const { createRouter, createWebHistory } = VueRouter;

const routes = [
    { path: "/login", component: Login },
    { path: "/", component: Home },
    { path: "/console", component: ConsoleLog },
    { path: "/edges", component: Edges },
    { path: "/packages", component: Packages },
    { path: "/variables", component: Variables },
    { path: "/repository", component: PackageRepository },
    { path: "/telemetry", component: TelemetryItems },
    { path: "/messages", component: MessageHandlers },
    { path: "/credentials", component: Credentials },
    { path: "/configuration", component: ConfigurationEditor }
];

export const router = createRouter({
    // History-mode routing (not hash) - Program.cs's IsSpa fallback serves index.html for any
    // unmatched sub-path under this base, so /console-next/packages works on a hard reload too.
    history: createWebHistory("/console-next/"),
    routes
});

router.beforeEach((to) => {
    if (to.path !== "/login" && !store.isLoggedIn) {
        return "/login";
    }
    if (to.path === "/login" && store.isLoggedIn) {
        return "/";
    }
    return true;
});
