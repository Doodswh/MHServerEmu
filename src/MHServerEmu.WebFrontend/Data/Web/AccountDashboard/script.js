(function () {
    const state = {
        token: sessionStorage.getItem(dashboardConfig.tokenStorageKey) || "",
        loading: false
    };

    const elements = {
        hamburger: document.getElementById("hamburger"),
        navbarLinks: document.getElementById("navbarSupportedContent"),
        navbar: document.querySelector(".navbar-dark"),
        authPanel: document.getElementById("auth-panel"),
        dashboard: document.getElementById("dashboard"),
        loginForm: document.getElementById("login-form"),
        loginButton: document.getElementById("login-button"),
        clearButton: document.getElementById("clear-button"),
        logoutButton: document.getElementById("logout-button"),
        coaDashboardLink: document.getElementById("coa-dashboard-link"),
        loginMessage: document.getElementById("login-message"),
        sessionState: document.getElementById("session-state"),
        sessionDetail: document.getElementById("session-detail"),
        accountOverview: document.getElementById("account-overview"),
        headlineStats: document.getElementById("headline-stats"),
        characterRows: document.getElementById("character-rows")
    };

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
                    try {
                        localStorage.setItem(manager.storageKey, nextTheme);
                    } catch (error) {}
                    manager.applyTheme(nextTheme);
                });
            });

            if (typeof manager.mediaQuery.addEventListener === "function") {
                manager.mediaQuery.addEventListener("change", function (event) {
                    try {
                        if (localStorage.getItem(manager.storageKey)) {
                            return;
                        }
                    } catch (error) {}

                    manager.applyTheme(event.matches ? "dark" : "light");
                });
            }
        }
    };

    function initializeNavbar() {
        if (!elements.hamburger || !elements.navbarLinks) {
            return;
        }

        elements.hamburger.addEventListener("click", function () {
            const isOpen = elements.navbarLinks.classList.toggle("show");
            elements.hamburger.classList.toggle("open", isOpen);
            elements.hamburger.setAttribute("aria-expanded", isOpen ? "true" : "false");
        });

        elements.navbarLinks.querySelectorAll("a, button").forEach(function (item) {
            item.addEventListener("click", function () {
                if (window.innerWidth > 900) {
                    return;
                }

                elements.navbarLinks.classList.remove("show");
                elements.hamburger.classList.remove("open");
                elements.hamburger.setAttribute("aria-expanded", "false");
            });
        });

        window.addEventListener("resize", function () {
            if (window.innerWidth > 900) {
                elements.navbarLinks.classList.remove("show");
                elements.hamburger.classList.remove("open");
                elements.hamburger.setAttribute("aria-expanded", "false");
            }
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
        const refreshIntervalMs = 60000;
        const statusEndpoint = window.location.port === "8313"
            ? window.location.origin + "/ServerStatus"
            : window.location.origin + "/AuthServer/ServerStatus";

        if (!statusText || !statusDot) {
            return;
        }

        function setStatus(state, label) {
            statusDot.classList.remove("is-online", "is-offline", "is-loading");
            statusDot.classList.add(state);
            statusText.textContent = label;
        }

        function readPlayerCount(data) {
            if (!data || typeof data !== "object") {
                return null;
            }

            if (typeof data.PlayerManagerPlayers === "number") {
                return data.PlayerManagerPlayers;
            }

            if (typeof data.GisPlayers === "number") {
                return data.GisPlayers;
            }

            return null;
        }

        function updateStatus() {
            const xhr = new XMLHttpRequest();
            xhr.open("GET", statusEndpoint, true);
            xhr.timeout = 8000;

            xhr.onreadystatechange = function () {
                if (xhr.readyState !== 4) {
                    return;
                }

                if (xhr.status !== 200) {
                    setStatus("is-offline", "Server Offline");
                    return;
                }

                try {
                    const data = JSON.parse(xhr.responseText);
                    const playerCount = readPlayerCount(data);
                    const label = typeof playerCount === "number"
                        ? playerCount + " " + (playerCount === 1 ? "Player" : "Players") + " Online"
                        : "Server Online";

                    setStatus("is-online", label);
                } catch (error) {
                    setStatus("is-offline", "Server Offline");
                }
            };

            xhr.onerror = function () {
                setStatus("is-offline", "Server Offline");
            };

            xhr.ontimeout = function () {
                setStatus("is-offline", "Server Offline");
            };

            xhr.send();
        }

        updateStatus();
        window.setInterval(updateStatus, refreshIntervalMs);
    }

    function initializeServerDetailsModal() {
        const trigger = document.getElementById("server-details-trigger");
        const modal = document.getElementById("serverdetails");
        let backdrop = null;

        if (!trigger || !modal) {
            return;
        }

        function closeModal() {
            modal.classList.remove("show");
            modal.style.display = "none";
            modal.setAttribute("aria-hidden", "true");
            document.body.classList.remove("modal-open");

            if (backdrop) {
                backdrop.remove();
                backdrop = null;
            }
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

        trigger.addEventListener("click", function (event) {
            event.preventDefault();
            openModal();
        });

        modal.querySelectorAll("[data-dismiss='modal']").forEach(function (button) {
            button.addEventListener("click", function () {
                closeModal();
            });
        });

        modal.addEventListener("click", function (event) {
            if (event.target === modal) {
                closeModal();
            }
        });

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape" && modal.classList.contains("show")) {
                closeModal();
            }
        });
    }

    function setToken(token) {
        state.token = token || "";

        if (state.token) {
            sessionStorage.setItem(dashboardConfig.tokenStorageKey, state.token);
        } else {
            sessionStorage.removeItem(dashboardConfig.tokenStorageKey);
        }
    }

    async function request(path, options) {
        const headers = {
            "Content-Type": "application/json"
        };

        if (state.token) {
            headers.Authorization = "Bearer " + state.token;
        }

        const response = await fetch(window.location.origin + dashboardConfig.originSuffix + path, {
            method: "GET",
            headers: headers,
            ...options
        });

        let data = null;
        try {
            data = await response.json();
        } catch (error) {
            data = null;
        }

        return {
            ok: response.ok,
            status: response.status,
            data: data
        };
    }

    function setLoading(isLoading) {
        state.loading = isLoading;
        elements.loginButton.disabled = isLoading;
        elements.clearButton.disabled = isLoading;
        elements.logoutButton.disabled = isLoading;
    }

    function setMessage(text, tone) {
        elements.loginMessage.textContent = text || "";
        elements.loginMessage.className = "message" + (tone ? " " + tone : "");
    }

    function setSessionState(title, detail) {
        elements.sessionState.textContent = title;
        elements.sessionDetail.textContent = detail;
    }

    function showAuthenticatedView() {
        elements.authPanel.classList.add("hidden");
        elements.dashboard.classList.remove("hidden");
        elements.logoutButton.classList.remove("hidden");
    }

    function showSignedOutView() {
        elements.authPanel.classList.remove("hidden");
        elements.dashboard.classList.add("hidden");
        elements.accountOverview.innerHTML = "";
        elements.headlineStats.innerHTML = "";
        elements.characterRows.innerHTML = "";
        elements.coaDashboardLink.classList.add("hidden");
        elements.logoutButton.classList.add("hidden");
    }

    function isAdminUser(userLevel) {
        return userLevel === "Admin" || userLevel === 2 || userLevel === "2";
    }

    function clearPasswordField() {
        const passwordInput = elements.loginForm.elements.namedItem("password");
        if (passwordInput) {
            passwordInput.value = "";
        }
    }

    function clearLoginForm() {
        elements.loginForm.reset();
    }

    function formatValue(value, fallback) {
        if (value === null || value === undefined || value === "") {
            return fallback || "Unavailable";
        }

        return String(value);
    }

    function formatDuration(seconds) {
        const total = Number(seconds || 0);
        if (!Number.isFinite(total) || total <= 0) {
            return "0m";
        }

        const hours = Math.floor(total / 3600);
        const minutes = Math.floor((total % 3600) / 60);

        if (hours <= 0) {
            return minutes + "m";
        }

        return hours + "h " + minutes + "m";
    }

    function formatDateTime(value) {
        if (value === null || value === undefined || value === "") {
            return "Unavailable";
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return String(value);
        }

        const month = String(date.getMonth() + 1).padStart(2, "0");
        const day = String(date.getDate()).padStart(2, "0");
        const year = String(date.getFullYear());
        const hours = String(date.getHours()).padStart(2, "0");
        const minutes = String(date.getMinutes()).padStart(2, "0");
        const seconds = String(date.getSeconds()).padStart(2, "0");

        return month + "/" + day + "/" + year + " " + hours + ":" + minutes + ":" + seconds;
    }

    function appendCard(container, label, value, className) {
        const card = document.createElement("div");
        card.className = className;
        card.innerHTML = "<span class=\"card-label\"></span><strong></strong>";
        card.querySelector(".card-label").textContent = label;
        card.querySelector("strong").textContent = value;
        container.appendChild(card);
    }

    function renderDashboard(data) {
        const account = data.account || {};
        const stats = data.stats || {};
        const characters = Array.isArray(data.characters) ? data.characters : [];
        const userLevel = account.userLevel || account.UserLevel;

        elements.accountOverview.innerHTML = "";
        elements.headlineStats.innerHTML = "";
        elements.characterRows.innerHTML = "";
        elements.coaDashboardLink.classList.toggle("hidden", !isAdminUser(userLevel));

        [
            ["Player Name", formatValue(account.playerName || account.PlayerName)],
            ["Email", formatValue(account.email || account.Email)],
            ["Account ID", formatValue(account.accountId || account.AccountId)],
            ["User Level", formatValue(account.userLevel || account.UserLevel)],
            ["Flags", formatValue(account.flags || account.Flags, "None")],
            ["Last Logout", formatDateTime(account.lastLogout || account.LastLogout)]
        ].forEach(function (entry) {
            appendCard(elements.accountOverview, entry[0], entry[1], "info-card");
        });

        [
            ["Total Time Played", formatDuration(stats.totalTimePlayed || stats.TotalTimePlayed)],
            ["Characters", formatValue(stats.totalCharacters || stats.TotalCharacters, "0")],
            ["Highest Level", formatValue(stats.highestLevel || stats.HighestLevel, "0")]
        ].forEach(function (entry) {
            appendCard(elements.headlineStats, entry[0], entry[1], "stat-card");
        });

        if (!characters.length) {
            const row = document.createElement("tr");
            row.innerHTML = "<td colspan=\"4\"><div class=\"empty-state\">No saved characters were found for this account.</div></td>";
            elements.characterRows.appendChild(row);
            return;
        }

        characters.forEach(function (character) {
            const row = document.createElement("tr");
            row.innerHTML = [
                "<td></td>",
                "<td></td>",
                "<td></td>",
                "<td></td>"
            ].join("");

            const cells = row.querySelectorAll("td");
            cells[0].textContent = formatValue(character.name || character.Name);
            cells[1].textContent = formatValue(character.prototypeName || character.PrototypeName);
            cells[2].textContent = formatValue(character.level || character.Level, "0");
            cells[3].textContent = formatDuration(character.timePlayed || character.TimePlayed);
            elements.characterRows.appendChild(row);
        });
    }

    async function loadDashboard() {
        const result = await request(dashboardConfig.endpoints.data);
        if (!result.ok || !result.data) {
            if (result.status === 401) {
                setToken("");
                showSignedOutView();
                setSessionState("Signed out", (result.data && result.data.message) || "No active account session was found.");
                return;
            }

            throw new Error((result.data && result.data.message) || "Unable to load account data.");
        }

        renderDashboard(result.data);
        showAuthenticatedView();
        setSessionState("Signed in", "Account details and playtime are ready.");
    }

    async function restoreSession() {
        if (!state.token) {
            showSignedOutView();
            setSessionState("Awaiting sign-in", "Use your existing game account credentials to open the portal.");
            return;
        }

        const result = await request(dashboardConfig.endpoints.session);
        if (!result.ok || !result.data || !(result.data.authenticated || result.data.Authenticated)) {
            setToken("");
            showSignedOutView();
            setSessionState("Awaiting sign-in", (result.data && result.data.message) || "No active account session was found.");
            return;
        }

        await loadDashboard();
    }

    async function handleLogin(event) {
        event.preventDefault();
        setLoading(true);
        setMessage("", "");

        try {
            const form = new FormData(elements.loginForm);
            const payload = {
                Email: String(form.get("email") || "").trim(),
                Password: String(form.get("password") || "")
            };

            if (!payload.Email || !payload.Password) {
                setMessage("Enter both your email and password.", "error");
                return;
            }

            const result = await request(dashboardConfig.endpoints.login, {
                method: "POST",
                body: JSON.stringify(payload)
            });

            if (!result.ok || !result.data || !(result.data.authenticated || result.data.Authenticated)) {
                setToken("");
                showSignedOutView();
                clearPasswordField();
                setSessionState("Sign-in failed", "The portal could not validate those account credentials.");
                setMessage((result.data && result.data.message) || "Incorrect email or password.", "error");
                return;
            }

            setToken(result.data.token || result.data.Token || "");
            await loadDashboard();
            setMessage((result.data && result.data.message) || "Signed in.", "success");
        } catch (error) {
            setMessage(error.message || "Unable to reach the account portal.", "error");
        } finally {
            setLoading(false);
        }
    }

    async function handleLogout() {
        setLoading(true);

        try {
            await request(dashboardConfig.endpoints.logout, {
                method: "POST",
                body: "{}"
            });
        } finally {
            setToken("");
            showSignedOutView();
            clearLoginForm();
            setSessionState("Signed out", "You have been signed out of the account portal.");
            setMessage("Signed out.", "success");
            setLoading(false);
        }
    }

    themeManager.initialize();
    initializeNavbar();
    initializeNavbarStatus();
    initializeServerDetailsModal();

    elements.loginForm.addEventListener("submit", handleLogin);
    elements.clearButton.addEventListener("click", function () {
        elements.loginForm.reset();
        setMessage("", "");
    });
    elements.logoutButton.addEventListener("click", handleLogout);

    restoreSession().catch(function (error) {
        setToken("");
        showSignedOutView();
        setSessionState("Awaiting sign-in", "Session restore failed.");
        setMessage(error.message || "Unable to restore the previous session.", "error");
    });
}());
