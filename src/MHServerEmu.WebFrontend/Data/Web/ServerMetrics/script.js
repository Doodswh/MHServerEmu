if (dashboardConfig == null) {
    const dashboardConfig = {
        originSuffix: ""
    };
}

(function () {
    const DEFAULT_REFRESH_INTERVAL_MS = 60000;
    const NAVBAR_STATUS_INTERVAL_MS = 60000;
    const REMOTE_CONSOLE_TOKEN_KEY = "coa-remote-console-token";
    const ACCOUNT_DASHBOARD_TOKEN_KEY = "coa-account-portal-token";

    function readStoredToken(key) {
        return window.sessionStorage.getItem(key) || window.localStorage.getItem(key) || "";
    }

    function writeStoredToken(key, value) {
        if (value) {
            window.sessionStorage.setItem(key, value);
            window.localStorage.setItem(key, value);
        } else {
            window.sessionStorage.removeItem(key);
            window.localStorage.removeItem(key);
        }
    }

    const elements = {
        hamburger: document.getElementById("hamburger"),
        navbarLinks: document.getElementById("navbarSupportedContent"),
        navbar: document.querySelector(".navbar-dark"),
        refreshIntervalSelect: document.getElementById("refresh-interval-select"),
        lastSyncLabel: document.getElementById("last-sync-label"),
        activePanelLabel: document.getElementById("active-panel-label")
    };
    const state = {
        refreshTimer: null,
        refreshIntervalMs: DEFAULT_REFRESH_INTERVAL_MS,
        activeTabName: "server-status",
        gameMetricsSelectedMetric: "",
        regionMetricsSelectedGame: "",
        authenticated: false,
        whitelisted: true,
        session: null,
        token: readStoredToken(REMOTE_CONSOLE_TOKEN_KEY) || "",
        accountToken: readStoredToken(ACCOUNT_DASHBOARD_TOKEN_KEY) || "",
        autoLoginAttempted: false
    };

    const apiUtil = {
        async request(path, options) {
            const requestOptions = Object.assign({
                headers: {
                    "Content-Type": "application/json"
                }
            }, options || {});

            const headers = Object.assign({}, requestOptions.headers || {});
            if (!headers.Authorization && state.token) {
                headers.Authorization = "Bearer " + state.token;
            }

            requestOptions.headers = headers;

            const response = await fetch(window.location.origin + dashboardConfig.originSuffix + path, requestOptions);

            let payload = null;
            const contentType = response.headers.get("content-type") || "";
            if (contentType.includes("application/json")) {
                payload = await response.json();
            } else {
                const text = await response.text();
                payload = text ? { message: text } : null;
            }

            return {
                ok: response.ok,
                status: response.status,
                data: payload
            };
        },
        get(path, callback) {
            this.request(path, { method: "GET" })
                .then(function (result) { callback(result && result.ok ? result.data : null); })
                .catch(function () { callback(null); });
        }
    };

    const htmlUtil = {
        createEmptyState(parent, message) {
            const emptyState = document.createElement("div");
            emptyState.className = "empty-state";
            emptyState.textContent = message;
            parent.appendChild(emptyState);
        },
        createMetricList(parent, listData) {
            const list = document.createElement("ul");
            list.className = "metric-list";
            listData.forEach(function (item) {
                const li = document.createElement("li");
                li.textContent = item;
                list.appendChild(li);
            });
            parent.appendChild(list);
        },
        createTable(parent, tableData) {
            const container = document.createElement("div");
            container.className = "table-container";
            const table = document.createElement("table");
            tableData.forEach(function (rowData, rowIndex) {
                const row = document.createElement("tr");
                rowData.forEach(function (cellData) {
                    const cell = document.createElement(rowIndex === 0 ? "th" : "td");
                    cell.textContent = cellData;
                    row.appendChild(cell);
                });
                table.appendChild(row);
            });
            container.appendChild(table);
            parent.appendChild(container);
        }
    };

    const stringUtil = {
        bigIntToHexString(value) {
            return BigInt(value).toString(16).toUpperCase();
        },
        formatTimeDiff(timeMs) {
            const msPerSecond = 1000;
            const msPerMinute = msPerSecond * 60;
            const msPerHour = msPerMinute * 60;
            const hours = Math.floor(timeMs / msPerHour);
            const minutes = Math.floor((timeMs % msPerHour) / msPerMinute);
            const seconds = Math.floor((timeMs % msPerMinute) / msPerSecond);
            return [hours, minutes, seconds].map(function (part) {
                return String(part).padStart(2, "0");
            }).join(":");
        },
        formatClock(date) {
            return date.toLocaleTimeString([], {
                hour: "2-digit",
                minute: "2-digit",
                second: "2-digit"
            });
        }
    };

    function storeRemoteConsoleToken(token) {
        state.token = token || "";
        writeStoredToken(REMOTE_CONSOLE_TOKEN_KEY, state.token);
    }

    async function tryAccountPortalAutoLogin() {
        if (state.autoLoginAttempted || state.authenticated || !state.whitelisted || !state.accountToken) {
            return false;
        }

        state.autoLoginAttempted = true;

        try {
            const result = await apiUtil.request("/RemoteConsole/AccountSessionLogin", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Authorization": "Bearer " + state.accountToken
                },
                body: "{}"
            });

            if (!result.ok || !result.data || !result.data.authenticated) {
                return false;
            }

            storeRemoteConsoleToken(result.data.token || "");
            state.authenticated = true;
            state.session = result.data.session || null;
            return true;
        } catch (error) {
            return false;
        }
    }

    async function refreshRemoteConsoleSession() {
        try {
            const result = await apiUtil.request("/RemoteConsole/Session", { method: "GET" });
            const data = result.data || {};
            state.authenticated = !!data.authenticated;
            state.whitelisted = data.whitelisted !== false;
            state.session = data.session || null;

            if (!state.authenticated) {
                storeRemoteConsoleToken("");
            }

            if (!state.authenticated && state.whitelisted) {
                await tryAccountPortalAutoLogin();
            }
        } catch (error) {
        }
    }

    const themeManager = {
        storageKey: "coa-theme",
        root: document.documentElement,
        mediaQuery: window.matchMedia("(prefers-color-scheme: dark)"),
        getPreferredTheme() {
            try {
                return localStorage.getItem(this.storageKey) || (this.mediaQuery.matches ? "dark" : "light");
            } catch (error) {
                return this.mediaQuery.matches ? "dark" : "light";
            }
        },
        applyTheme(theme) {
            this.root.setAttribute("data-theme", theme);
            this.root.style.colorScheme = theme;
            document.querySelectorAll("[data-theme-label]").forEach(function (label) {
                label.textContent = theme === "dark" ? "Light" : "Dark";
            });
        },
        initialize() {
            const manager = this;
            manager.applyTheme(manager.getPreferredTheme());
            document.querySelectorAll("[data-theme-toggle]").forEach(function (button) {
                button.addEventListener("click", function () {
                    const nextTheme = manager.root.getAttribute("data-theme") === "dark" ? "light" : "dark";
                    try { localStorage.setItem(manager.storageKey, nextTheme); } catch (error) {}
                    manager.applyTheme(nextTheme);
                });
            });
        }
    };

    function initializeNavbar() {
        if (!elements.hamburger || !elements.navbarLinks) return;
        elements.hamburger.addEventListener("click", function () {
            const isOpen = elements.navbarLinks.classList.toggle("show");
            elements.hamburger.classList.toggle("open", isOpen);
            elements.hamburger.setAttribute("aria-expanded", isOpen ? "true" : "false");
        });
        elements.navbarLinks.querySelectorAll("a, button").forEach(function (item) {
            item.addEventListener("click", function () {
                if (window.innerWidth > 900) return;
                elements.navbarLinks.classList.remove("show");
                elements.hamburger.classList.remove("open");
                elements.hamburger.setAttribute("aria-expanded", "false");
            });
        });
        if (elements.navbar) {
            const updateScrolledState = function () {
                elements.navbar.classList.toggle("scrolled", window.scrollY > window.innerHeight * 1);
            };
            updateScrolledState();
            window.addEventListener("scroll", updateScrolledState, { passive: true });
        }
    }

    function initializeNavbarStatus() {
        const statusText = document.getElementById("navbar-server-status-text");
        const statusDot = document.getElementById("navbar-server-status-dot");
        const statusEndpoint = window.location.pathname.toLowerCase().indexOf("/authserver/") === 0
            ? window.location.origin + "/AuthServer/ServerStatus"
            : window.location.origin + "/ServerStatus";
        if (!statusText || !statusDot) return;
        function setStatus(statusClass, label) {
            statusDot.classList.remove("is-online", "is-offline", "is-loading");
            statusDot.classList.add(statusClass);
            statusText.textContent = label;
        }
        function readPlayerCount(data) {
            if (!data || typeof data !== "object") return null;
            if (typeof data.PlayerManagerPlayers === "number") return data.PlayerManagerPlayers;
            if (typeof data.GisPlayers === "number") return data.GisPlayers;
            return null;
        }
        function updateStatus() {
            const xhr = new XMLHttpRequest();
            xhr.open("GET", statusEndpoint, true);
            xhr.timeout = 8000;
            xhr.onreadystatechange = function () {
                if (xhr.readyState !== 4) return;
                if (xhr.status !== 200) {
                    setStatus("is-offline", "Server Offline");
                    return;
                }
                try {
                    const data = JSON.parse(xhr.responseText);
                    const playerCount = readPlayerCount(data);
                    setStatus("is-online", typeof playerCount === "number" ? playerCount + " " + (playerCount === 1 ? "Player" : "Players") + " Online" : "Server Online");
                } catch (error) {
                    setStatus("is-offline", "Server Offline");
                }
            };
            xhr.onerror = function () { setStatus("is-offline", "Server Offline"); };
            xhr.ontimeout = function () { setStatus("is-offline", "Server Offline"); };
            xhr.send();
        }
        updateStatus();
        window.setInterval(updateStatus, NAVBAR_STATUS_INTERVAL_MS);
    }

    function initializeServerDetailsModal() {
        const trigger = document.getElementById("server-details-trigger");
        const modal = document.getElementById("serverdetails");
        let backdrop = null;
        if (!trigger || !modal) return;
        function closeModal() {
            modal.classList.remove("show");
            modal.style.display = "none";
            modal.setAttribute("aria-hidden", "true");
            document.body.classList.remove("modal-open");
            if (backdrop) { backdrop.remove(); backdrop = null; }
        }
        function openModal() {
            modal.classList.add("show");
            modal.style.display = "block";
            modal.setAttribute("aria-hidden", "false");
            document.body.classList.add("modal-open");
            backdrop = document.createElement("div");
            backdrop.className = "modal-backdrop fade show";
            document.body.appendChild(backdrop);
        }
        trigger.addEventListener("click", function (event) { event.preventDefault(); openModal(); });
        modal.querySelectorAll("[data-dismiss='modal']").forEach(function (button) { button.addEventListener("click", closeModal); });
        modal.addEventListener("click", function (event) { if (event.target === modal) closeModal(); });
        document.addEventListener("keydown", function (event) { if (event.key === "Escape" && modal.classList.contains("show")) closeModal(); });
    }

    function formatRefreshIntervalLabel(intervalMs) {
        return Math.round(intervalMs / 1000) + " sec";
    }

    function initializeRefreshIntervalSelect() {
        if (!elements.refreshIntervalSelect) return;
        elements.refreshIntervalSelect.value = String(state.refreshIntervalMs);
        elements.refreshIntervalSelect.addEventListener("change", function () {
            const nextIntervalMs = Number(elements.refreshIntervalSelect.value) || DEFAULT_REFRESH_INTERVAL_MS;
            state.refreshIntervalMs = nextIntervalMs;
            if (!document.hidden) startAutoRefresh();
        });
    }

    function setLastSync(tabName) {
        if (!elements.lastSyncLabel || tabName !== state.activeTabName) return;
        elements.lastSyncLabel.textContent = stringUtil.formatClock(new Date());
    }

    function setActivePanel(label) {
        if (elements.activePanelLabel) elements.activePanelLabel.textContent = label;
    }

    const serverStatusTab = {
        tabName: "server-status",
        panelLabel: "Server Status",
        initialize() {
            document.getElementById("server-status-button").onclick = () => this.requestData();
        },
        requestData() {
            apiUtil.get("/ServerStatus", (data) => this.onDataReceived(data));
        },
        onDataReceived(data) {
            const container = document.getElementById("server-status-container");
            container.innerHTML = "";
            if (data == null) {
                htmlUtil.createEmptyState(container, "Server status is currently unavailable.");
                return;
            }
            htmlUtil.createMetricList(container, [
                "Uptime: " + stringUtil.formatTimeDiff((data.CurrentTime - data.StartupTime) * 1000),
                "[GameInstance] Games: " + data.GisGames + " | Players: " + data.GisPlayers,
                "[Leaderboard] Leaderboards: " + data.Leaderboards,
                "[PlayerManager] Games: " + data.PlayerManagerGames + " | Players: " + data.PlayerManagerPlayers + " | Sessions: " + data.PlayerManagerActiveSessions + " [" + data.PlayerManagerPendingSessions + "]",
                "[GroupingManager] Players: " + data.GroupingManagerPlayers,
                "[Frontend] Connections: " + data.FrontendConnections + " | Clients: " + data.FrontendClients,
                "[Auth] Handlers: " + data.WebFrontendHandlers + " | Handled Requests: " + data.WebFrontendHandledRequests
            ]);
            setLastSync(this.tabName);
        }
    };

    const metricsTab = {
        tabName: "metrics",
        panelLabel: "Metrics",
        regionReportsByGame: {},
        initialize() {
            document.getElementById("metrics-button").onclick = () => this.requestData();
            document.getElementById("metrics-game-select").onchange = () => this.onGameMetricSelectChanged();
            document.getElementById("metrics-region-game-select").onchange = () => this.onRegionGameSelectChanged();
            document.getElementById("metrics-region-sort-select").onchange = () => this.onRegionSortChanged();
            document.getElementById("metrics-region-hot-only").onchange = () => this.onRegionHotOnlyChanged();
        },
        requestData() {
            apiUtil.get("/Metrics/Performance", (data) => this.onDataReceived(data));
        },
        onDataReceived(data) {
            if (data == null) {
                this.showEmptyState();
                return;
            }
            this.updateReportMetadata(data);
            this.updateMemoryMetrics(data.Memory);
            this.updateGameMetrics(data.Games);
            this.updateRegionMetrics(data.RegionsByGame || {});
            setLastSync(this.tabName);
        },
        showEmptyState() {
            const metadataContainer = document.getElementById("metrics-report-metadata");
            const memoryContainer = document.getElementById("metrics-memory-container");
            const gameMetricContainer = document.getElementById("metrics-game-container");
            const gameMetricSelect = document.getElementById("metrics-game-select");
            const regionMetricContainer = document.getElementById("metrics-regions-container");
            const regionMetricSummary = document.getElementById("metrics-regions-summary");
            const regionMetricSelect = document.getElementById("metrics-region-game-select");
            metadataContainer.innerHTML = "";
            memoryContainer.innerHTML = "";
            gameMetricContainer.innerHTML = "";
            gameMetricSelect.innerHTML = "";
            regionMetricContainer.innerHTML = "";
            regionMetricSummary.innerHTML = "";
            regionMetricSelect.innerHTML = "";
            htmlUtil.createEmptyState(metadataContainer, "Metrics report metadata is unavailable.");
            htmlUtil.createEmptyState(memoryContainer, "Memory metrics are unavailable.");
            htmlUtil.createEmptyState(gameMetricContainer, "Game metrics are unavailable.");
            htmlUtil.createEmptyState(regionMetricContainer, "Hot-region metrics are unavailable.");
        },
        onGameMetricSelectChanged() {
            const select = document.getElementById("metrics-game-select");
            const metric = select.options.length ? select.options[select.selectedIndex].value : "";
            state.gameMetricsSelectedMetric = metric;
            this.selectGameMetric(metric);
        },
        onRegionGameSelectChanged() {
            const select = document.getElementById("metrics-region-game-select");
            const gameId = select.options.length ? select.options[select.selectedIndex].value : "";
            state.regionMetricsSelectedGame = gameId;
            this.selectRegionGame(gameId);
        },
        onRegionSortChanged() {
            const select = document.getElementById("metrics-region-sort-select");
            state.regionMetricsSortMode = select.options.length ? select.options[select.selectedIndex].value : "hotness";
            this.selectRegionGame(state.regionMetricsSelectedGame);
        },
        onRegionHotOnlyChanged() {
            state.regionMetricsHotOnly = !!document.getElementById("metrics-region-hot-only").checked;
            this.selectRegionGame(state.regionMetricsSelectedGame);
        },
        updateReportMetadata(data) {
            const metadataContainer = document.getElementById("metrics-report-metadata");
            metadataContainer.innerHTML = "";
            htmlUtil.createMetricList(metadataContainer, ["Report 0x" + stringUtil.bigIntToHexString(data.Id)]);
        },
        updateMemoryMetrics(data) {
            const memoryContainer = document.getElementById("metrics-memory-container");
            memoryContainer.innerHTML = "";
            htmlUtil.createMetricList(memoryContainer, [
                "GCIndex: " + data.GCIndex,
                "GCCounts: Gen0=" + data.GCCountGen0 + ", Gen1=" + data.GCCountGen1 + ", Gen2=" + data.GCCountGen2,
                "HeapSizeBytes: " + data.HeapSizeBytes.toLocaleString() + " / " + data.TotalCommittedBytes.toLocaleString(),
                "PauseTimePercentage: " + data.PauseTimePercentage + "%",
                "PauseDuration: " + this.formatTracker(data.PauseDuration)
            ]);
        },
        updateGameMetrics(data) {
            const select = document.getElementById("metrics-game-select");
            const container = document.getElementById("metrics-game-container");
            const dataMap = new Map();
            let selectedMetric = state.gameMetricsSelectedMetric;
            select.innerHTML = "";
            container.innerHTML = "";
            for (const key in data) {
                const entry = data[key];
                for (const metric in entry) {
                    const value = entry[metric];
                    if (!dataMap.has(metric)) dataMap.set(metric, [["GameId", "Avg", "Mdn", "Last", "Min", "Max"]]);
                    dataMap.get(metric).push([
                        "0x" + stringUtil.bigIntToHexString(key),
                        value.Average.toFixed(2),
                        value.Median.toFixed(2),
                        value.Last.toFixed(2),
                        value.Min.toFixed(2),
                        value.Max.toFixed(2)
                    ]);
                }
            }
            if (!dataMap.size) {
                htmlUtil.createEmptyState(container, "No game metrics were returned.");
                return;
            }
            dataMap.forEach(function (tableData, metric) {
                const option = document.createElement("option");
                option.value = metric;
                option.textContent = metric;
                select.appendChild(option);
                const subcontainer = document.createElement("div");
                subcontainer.id = metric + "-subcontainer";
                subcontainer.className = "game-metric-subcontainer";
                htmlUtil.createTable(subcontainer, tableData);
                container.appendChild(subcontainer);
            });
            if (!selectedMetric || !dataMap.has(selectedMetric)) selectedMetric = dataMap.keys().next().value;
            select.value = selectedMetric;
            state.gameMetricsSelectedMetric = selectedMetric;
            this.selectGameMetric(selectedMetric);
        },
        updateRegionMetrics(data) {
            const select = document.getElementById("metrics-region-game-select");
            const container = document.getElementById("metrics-regions-container");
            const summary = document.getElementById("metrics-regions-summary");
            const previousGameId = state.regionMetricsSelectedGame;
            const gameIds = Object.keys(data).sort(function (left, right) {
                const leftId = BigInt(left);
                const rightId = BigInt(right);
                if (leftId < rightId) return -1;
                if (leftId > rightId) return 1;
                return 0;
            });

            this.regionReportsByGame = data;
            select.innerHTML = "";
            container.innerHTML = "";
            summary.innerHTML = "";

            if (!gameIds.length) {
                htmlUtil.createEmptyState(container, "No hot-region scheduler metrics were returned.");
                state.regionMetricsSelectedGame = "";
                return;
            }

            gameIds.forEach(function (gameId) {
                const reports = data[gameId] || [];
                const option = document.createElement("option");
                option.value = gameId;
                option.textContent = "Game 0x" + stringUtil.bigIntToHexString(gameId) + " (" + reports.length + " regions)";
                select.appendChild(option);
            });

            document.getElementById("metrics-region-sort-select").value = state.regionMetricsSortMode || "hotness";
            document.getElementById("metrics-region-hot-only").checked = !!state.regionMetricsHotOnly;

            const selectedGameId = previousGameId && data[previousGameId] ? previousGameId : gameIds[0];
            select.value = selectedGameId;
            state.regionMetricsSelectedGame = selectedGameId;
            this.selectRegionGame(selectedGameId);
        },
        selectGameMetric(metric) {
            Array.prototype.forEach.call(document.getElementsByClassName("game-metric-subcontainer"), function (subcontainer) {
                subcontainer.style.display = "none";
            });
            if (!metric) return;
            const selected = document.getElementById(metric + "-subcontainer");
            if (selected) selected.style.display = "block";
        },
        selectRegionGame(gameId) {
            const container = document.getElementById("metrics-regions-container");
            const summary = document.getElementById("metrics-regions-summary");
            container.innerHTML = "";
            summary.innerHTML = "";

            if (!gameId) return;

            const reports = this.getFilteredAndSortedReports(gameId);
            const allReports = (this.regionReportsByGame[gameId] || []).slice();

            this.renderRegionSummary(summary, allReports, reports);

            if (!reports.length) {
                htmlUtil.createEmptyState(container, state.regionMetricsHotOnly ? "No hot regions matched the current filter." : "No region entries matched the current sort/filter state.");
                return;
            }

            this.renderRegionTable(container, reports);
        },
        getFilteredAndSortedReports(gameId) {
            const reports = (this.regionReportsByGame[gameId] || []).slice();
            const filtered = state.regionMetricsHotOnly
                ? reports.filter((report) => this.isHotRegion(report))
                : reports;

            return filtered.sort((left, right) => {
                const comparison = this.compareReports(left, right, state.regionMetricsSortMode || "hotness");
                if (comparison !== 0) return comparison;

                const leftRegionId = BigInt(left.RegionId);
                const rightRegionId = BigInt(right.RegionId);
                if (leftRegionId < rightRegionId) return -1;
                if (leftRegionId > rightRegionId) return 1;
                return 0;
            });
        },
        compareReports(left, right, sortMode) {
            if (sortMode === "region") {
                const leftRegionId = BigInt(left.RegionId);
                const rightRegionId = BigInt(right.RegionId);
                if (leftRegionId < rightRegionId) return -1;
                if (leftRegionId > rightRegionId) return 1;
                return 0;
            }

            if (sortMode === "players") {
                if (left.PlayerCount !== right.PlayerCount) return right.PlayerCount - left.PlayerCount;
                return this.getHotnessScore(right) - this.getHotnessScore(left);
            }

            if (sortMode === "pending") {
                return this.getPendingCount(right) - this.getPendingCount(left);
            }

            if (sortMode === "transfers") {
                return this.getPhaseSortScore(right.Transfers) - this.getPhaseSortScore(left.Transfers);
            }

            if (sortMode === "aoi") {
                return this.getPhaseSortScore(right.Aoi) - this.getPhaseSortScore(left.Aoi);
            }

            if (sortMode === "events") {
                return this.getPhaseSortScore(right.Events) - this.getPhaseSortScore(left.Events);
            }

            return this.getHotnessScore(right) - this.getHotnessScore(left);
        },
        renderRegionTable(parent, reports) {
            const container = document.createElement("div");
            container.className = "table-container";
            const table = document.createElement("table");
            table.className = "metrics-region-table";

            const thead = document.createElement("thead");
            const headerRow = document.createElement("tr");
            ["RegionId", "Prototype", "Players", "Match", "Hot Phases", "Transfers", "AOI", "Events"].forEach(function (label) {
                const cell = document.createElement("th");
                cell.textContent = label;
                headerRow.appendChild(cell);
            });
            thead.appendChild(headerRow);
            table.appendChild(thead);

            const tbody = document.createElement("tbody");
            reports.forEach((report) => {
                const row = document.createElement("tr");
                row.className = this.isHotRegion(report) ? "metrics-region-row is-hot" : "metrics-region-row";
                this.appendTextCell(row, "0x" + stringUtil.bigIntToHexString(report.RegionId));
                this.appendTextCell(row, report.PrototypeName || "Unknown");
                this.appendTextCell(row, String(report.PlayerCount));
                this.appendTextCell(row, String(report.MatchNumber));
                row.appendChild(this.createHotPhaseCell(report));
                row.appendChild(this.createPhaseCell(report.Transfers, "Transfers"));
                row.appendChild(this.createPhaseCell(report.Aoi, "AOI"));
                row.appendChild(this.createPhaseCell(report.Events, "Events"));
                tbody.appendChild(row);
            });
            table.appendChild(tbody);
            container.appendChild(table);
            parent.appendChild(container);
        },
        appendTextCell(row, text) {
            const cell = document.createElement("td");
            cell.textContent = text;
            row.appendChild(cell);
        },
        createHotPhaseCell(report) {
            const cell = document.createElement("td");
            cell.className = "metrics-region-hot-cell";
            const phases = [
                { label: "Transfers", className: "transfers", data: report.Transfers },
                { label: "AOI", className: "aoi", data: report.Aoi },
                { label: "Events", className: "events", data: report.Events }
            ];
            const wrapper = document.createElement("div");
            wrapper.className = "metrics-phase-badge-list";
            phases.forEach((phase) => {
                const badge = document.createElement("span");
                badge.className = "metrics-phase-badge " + phase.className + (phase.data.IsHot ? " is-hot" : "");
                badge.textContent = phase.data.IsHot ? phase.label + " HOT" : phase.label + " OK";
                wrapper.appendChild(badge);
            });
            cell.appendChild(wrapper);
            return cell;
        },
        createPhaseCell(phase, label) {
            const cell = document.createElement("td");
            cell.className = "metrics-region-phase-cell";

            const status = document.createElement("span");
            status.className = "metrics-phase-badge inline " + label.toLowerCase() + (phase.IsHot ? " is-hot" : "");
            status.textContent = phase.IsHot ? "HOT" : "OK";

            const metrics = document.createElement("div");
            metrics.className = "metrics-region-phase-metrics";
            metrics.textContent = "pending=" + phase.Pending + " | avg=" + phase.AverageElapsedMilliseconds.toFixed(2) + " ms | last=" + phase.LastElapsedMilliseconds.toFixed(2) + " ms | budget=" + phase.LastBudget + " | processed=" + phase.LastProcessed;

            cell.appendChild(status);
            cell.appendChild(metrics);
            return cell;
        },
        renderRegionSummary(parent, allReports, visibleReports) {
            const grid = document.createElement("div");
            grid.className = "metrics-region-summary-grid";

            const hotRegions = allReports.filter((report) => this.isHotRegion(report)).length;
            const visibleHotRegions = visibleReports.filter((report) => this.isHotRegion(report)).length;
            const totalPlayers = allReports.reduce(function (sum, report) { return sum + report.PlayerCount; }, 0);
            const hottest = allReports.reduce((best, report) => {
                if (!best) return report;
                return this.getHotnessScore(report) > this.getHotnessScore(best) ? report : best;
            }, null);

            this.appendSummaryCard(grid, "Regions", String(visibleReports.length) + " / " + String(allReports.length), state.regionMetricsHotOnly ? "Filtered to hot regions only" : "Visible regions in this game report");
            this.appendSummaryCard(grid, "Hot Regions", String(visibleHotRegions) + " / " + String(hotRegions), "Regions flagged hot in any scheduler phase");
            this.appendSummaryCard(grid, "Players", String(totalPlayers), "Players across all reported regions");
            this.appendSummaryCard(
                grid,
                "Top Hotspot",
                hottest ? hottest.PrototypeName + " (0x" + stringUtil.bigIntToHexString(hottest.RegionId) + ")" : "n/a",
                hottest ? this.formatRegionPhaseShort(hottest.Aoi) + " | " + this.formatRegionPhaseShort(hottest.Events) : "n/a"
            );

            parent.appendChild(grid);
        },
        appendSummaryCard(parent, label, value, detail) {
            const card = document.createElement("div");
            card.className = "metrics-region-summary-card";

            const labelNode = document.createElement("div");
            labelNode.className = "metrics-region-summary-label";
            labelNode.textContent = label;

            const valueNode = document.createElement("div");
            valueNode.className = "metrics-region-summary-value";
            valueNode.textContent = value;

            const detailNode = document.createElement("div");
            detailNode.className = "metrics-region-summary-detail";
            detailNode.textContent = detail;

            card.appendChild(labelNode);
            card.appendChild(valueNode);
            card.appendChild(detailNode);
            parent.appendChild(card);
        },
        isHotRegion(report) {
            return report.Transfers.IsHot || report.Aoi.IsHot || report.Events.IsHot;
        },
        getPendingCount(report) {
            return report.Transfers.Pending + report.Aoi.Pending + report.Events.Pending;
        },
        getPhaseSortScore(phase) {
            return (phase.IsHot ? 100000 : 0) + (phase.Pending * 100) + Math.round(phase.AverageElapsedMilliseconds * 10) + Math.round(phase.LastElapsedMilliseconds * 5);
        },
        getHotnessScore(report) {
            let score = 0;
            score += report.Transfers.IsHot ? 1 : 0;
            score += report.Aoi.IsHot ? 2 : 0;
            score += report.Events.IsHot ? 2 : 0;
            score += this.getPendingCount(report);
            return score;
        },
        formatRegionPhaseShort(phase) {
            const status = phase.IsHot ? "HOT" : "OK";
            return status + ", pending=" + phase.Pending + ", avg=" + phase.AverageElapsedMilliseconds.toFixed(2) + " ms";
        },
        formatTracker(tracker) {
            return "avg=" + tracker.Average.toFixed(2) + ", mdn=" + tracker.Median.toFixed(2) + ", last=" + tracker.Last.toFixed(2) + ", min=" + tracker.Min.toFixed(2) + ", max=" + tracker.Max.toFixed(2);
        }
    };

    const regionReportTab = {
        tabName: "region-report",
        panelLabel: "Region",
        initialize() {
            document.getElementById("region-report-button").onclick = () => this.requestData();
        },
        requestData() {
            apiUtil.get("/RegionReport", (data) => this.onDataReceived(data));
        },
        onDataReceived(data) {
            const container = document.getElementById("region-report-container");
            container.innerHTML = "";
            if (data == null || !Array.isArray(data.Regions) || !data.Regions.length) {
                htmlUtil.createEmptyState(container, "Region report data is currently unavailable.");
                return;
            }
            const tableData = [["GameId", "RegionId", "Name", "DifficultyTier", "Uptime"]];
            let gameId = 0;
            data.Regions.forEach(function (region) {
                let gameText = "";
                if (gameId !== region.GameId) {
                    gameId = region.GameId;
                    gameText = "0x" + stringUtil.bigIntToHexString(gameId);
                }
                tableData.push([
                    gameText,
                    "0x" + stringUtil.bigIntToHexString(region.RegionId),
                    region.Name,
                    region.DifficultyTier,
                    region.Uptime
                ]);
            });
            htmlUtil.createTable(container, tableData);
            setLastSync(this.tabName);
        }
    };

    const tabs = [serverStatusTab, metricsTab, regionReportTab];

    const tabManager = {
        initialize() {
            tabs.forEach(function (tab) {
                document.getElementById(tab.tabName + "-tab-button").addEventListener("click", function () {
                    tabManager.openTab(tab);
                });
                tab.initialize();
            });
            this.openTab(serverStatusTab);
        },
        openTab(tab) {
            tabs.forEach(function (entry) {
                const panel = document.getElementById(entry.tabName + "-tab");
                const button = document.getElementById(entry.tabName + "-tab-button");
                const isActive = entry.tabName === tab.tabName;
                panel.classList.toggle("hidden", !isActive);
                button.classList.toggle("active", isActive);
                button.setAttribute("aria-selected", isActive ? "true" : "false");
            });
            state.activeTabName = tab.tabName;
            setActivePanel(tab.panelLabel);
            tab.requestData();
        }
    };

    function refreshAllData() {
        tabs.forEach(function (tab) { tab.requestData(); });
    }
    function stopAutoRefresh() {
        if (state.refreshTimer) {
            window.clearInterval(state.refreshTimer);
            state.refreshTimer = null;
        }
    }
    function startAutoRefresh() {
        stopAutoRefresh();
        refreshAllData();
        state.refreshTimer = window.setInterval(refreshAllData, state.refreshIntervalMs);
    }

    document.addEventListener("visibilitychange", function () {
        if (document.hidden) stopAutoRefresh();
        else startAutoRefresh();
    });

    themeManager.initialize();
    initializeNavbar();
    initializeNavbarStatus();
    initializeServerDetailsModal();
    initializeRefreshIntervalSelect();
    refreshRemoteConsoleSession().finally(function () {
        tabManager.initialize();
        startAutoRefresh();
    });
}());



