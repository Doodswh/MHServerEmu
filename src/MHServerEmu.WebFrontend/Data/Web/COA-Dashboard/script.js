if (typeof dashboardConfig === "undefined") {
  var dashboardConfig = {
    originSuffix: ""
  };
}

(function () {
  const POLL_INTERVAL_MS = 2000;
  const NAVBAR_STATUS_POLL_INTERVAL_MS = 30000;
  const MAX_LINES = 300;
  const SESSION_STORAGE_KEY = "coa-remote-console-token";
  const ACCOUNT_DASHBOARD_TOKEN_KEY = "coa-account-portal-token";

  const state = {
    authenticated: false,
    whitelisted: true,
    session: null,
    token: window.sessionStorage.getItem(SESSION_STORAGE_KEY) || "",
    accountToken: window.sessionStorage.getItem(ACCOUNT_DASHBOARD_TOKEN_KEY) || "",
    latestSequence: 0,
    logs: [],
    commandLogs: [],
    activeTerminalTab: "standard",
    pollTimer: null,
    navbarStatusTimer: null,
    inFlight: false,
    autoLoginAttempted: false
  };

  const elements = {
    navbar: document.querySelector(".navbar"),
    navbarStatusDot: document.getElementById("navbar-server-status-dot"),
    navbarStatusText: document.getElementById("navbar-server-status-text"),
    hamburger: document.getElementById("hamburger"),
    navbarCollapse: document.getElementById("navbarSupportedContent"),
    navLinks: document.querySelectorAll("#navbarSupportedContent .nav-link"),
    hero: document.querySelector(".hero"),
    uptimeStat: document.getElementById("uptime-stat"),
    clientsStat: document.getElementById("clients-stat"),
    requestsStat: document.getElementById("requests-stat"),
    authPanel: document.querySelector(".login-panel"),
    loginForm: document.getElementById("login-form"),
    loginSubmit: document.getElementById("login-submit"),
    loginClear: document.getElementById("login-clear"),
    username: document.getElementById("username"),
    password: document.getElementById("password"),
    loginMessage: document.getElementById("login-message"),
    logoutButton: document.getElementById("logout-button"),
    terminalOutput: document.getElementById("terminal-output"),
    commandOutput: document.getElementById("command-output"),
    terminalTabs: document.querySelectorAll("[data-terminal-tab]"),
    logSequenceLabel: document.getElementById("log-sequence-label"),
    commandForm: document.getElementById("command-form"),
    commandInput: document.getElementById("command-input"),
    commandSubmit: document.getElementById("command-submit"),
    commandMessage: document.getElementById("command-message"),
    statusList: document.getElementById("status-list"),
    themeToggle: document.getElementById("theme-toggle")
  };

  function buildUrl(path) {
    return window.location.origin + (dashboardConfig.originSuffix || "") + path;
  }

  function applyThemeLabel(theme) {
    document.querySelectorAll("[data-theme-label]").forEach(function (label) {
      label.textContent = theme === "dark" ? "Light" : "Dark";
    });
  }

  async function apiRequest(path, options) {
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

    const response = await fetch(buildUrl(path), requestOptions);

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
  }

  function setMessage(element, text, tone) {
    element.textContent = text || "";
    element.className = "message" + (tone ? " " + tone : "");
  }

  function setConnectionState(label, tone) {
    if (!elements.navbarStatusText || !elements.navbarStatusDot) {
      return;
    }

    const stateClass = tone === "online"
      ? "is-online"
      : tone === "offline"
        ? "is-offline"
        : "is-loading";

    elements.navbarStatusText.textContent = label;
    elements.navbarStatusDot.className = "navbar-server-status-dot " + stateClass;
  }

  function formatDuration(totalSeconds) {
    const safe = Math.max(0, Math.floor(totalSeconds || 0));
    const hours = String(Math.floor(safe / 3600)).padStart(2, "0");
    const minutes = String(Math.floor((safe % 3600) / 60)).padStart(2, "0");
    const seconds = String(safe % 60).padStart(2, "0");
    return [hours, minutes, seconds].join(":");
  }

  function updateAuthUi() {
    if (elements.hero) {
      elements.hero.classList.toggle("authenticated", state.authenticated);
    }
    if (elements.authPanel) {
      elements.authPanel.classList.toggle("hidden", state.authenticated);
    }
    elements.logoutButton.classList.toggle("hidden", !state.authenticated);
    elements.commandInput.disabled = !state.authenticated;
    elements.commandSubmit.disabled = !state.authenticated;
    elements.loginSubmit.disabled = !state.whitelisted;
  }

  function setNavbarExpanded(expanded) {
    if (!elements.hamburger || !elements.navbarCollapse) {
      return;
    }

    elements.hamburger.classList.toggle("open", expanded);
    elements.hamburger.setAttribute("aria-expanded", expanded ? "true" : "false");
    elements.navbarCollapse.classList.toggle("show", expanded);
  }

  function handleScrollState() {
    if (!elements.navbar) {
      return;
    }

    elements.navbar.classList.toggle("scrolled", window.scrollY > window.innerHeight * 1);
  }

  async function refreshNavbarServerStatus() {
    try {
      const result = await apiRequest("/ServerStatus", { method: "GET" });
      if (!result.ok || !result.data) {
        setConnectionState("Server offline", "offline");
        return;
      }

      const playerCount = typeof result.data.PlayerManagerPlayers === "number"
        ? result.data.PlayerManagerPlayers
        : typeof result.data.GisPlayers === "number"
          ? result.data.GisPlayers
          : null;
      const label = typeof playerCount === "number"
        ? playerCount + " " + (playerCount === 1 ? "Player" : "Players") + " Online"
        : "Server Online";

      setConnectionState(label, "online");
      updateStatus(result.data);
    } catch (error) {
      setConnectionState("Server offline", "offline");
    }
  }

  async function tryAccountPortalAutoLogin() {
    if (state.autoLoginAttempted || state.authenticated || !state.whitelisted || !state.accountToken) {
      return false;
    }

    state.autoLoginAttempted = true;

    try {
      const result = await apiRequest("/RemoteConsole/AccountSessionLogin", {
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

      storeToken(result.data.token || "");
      state.authenticated = true;
      state.session = result.data.session || null;
      state.logs = [];
      state.commandLogs = [];
      state.latestSequence = 0;
      updateAuthUi();
      setMessage(elements.loginMessage, result.data.message || "Authenticated.", "success");
      setMessage(elements.commandMessage, "Remote console unlocked.", "success");
      startPolling();
      return true;
    } catch (error) {
      return false;
    }
  }

  function storeToken(token) {
    state.token = token || "";
    if (state.token) {
      window.sessionStorage.setItem(SESSION_STORAGE_KEY, state.token);
    } else {
      window.sessionStorage.removeItem(SESSION_STORAGE_KEY);
    }
  }

  function updateStatus(status) {
    if (!status) {
      return;
    }

    const now = Number(status.CurrentTime || 0);
    const started = Number(status.StartupTime || 0);
    const uptimeSeconds = Math.max(0, now - started);

    elements.uptimeStat.textContent = formatDuration(uptimeSeconds);
    elements.clientsStat.textContent = String(status.FrontendClients || 0);
    elements.requestsStat.textContent = Number(status.WebFrontendHandledRequests || 0).toLocaleString();

    const rows = [
      ["Game instances", String(status.GisGames || 0)],
      ["Players in games", String(status.GisPlayers || 0)],
      ["Frontend connections", String(status.FrontendConnections || 0)],
      ["Frontend clients", String(status.FrontendClients || 0)],
      ["Player sessions", String(status.PlayerManagerActiveSessions || 0)],
      ["Queued sessions", String(status.PlayerManagerPendingSessions || 0)],
      ["Dashboard handlers", String(status.WebFrontendHandlers || 0)],
      ["Handled requests", Number(status.WebFrontendHandledRequests || 0).toLocaleString()]
    ];

    elements.statusList.innerHTML = "";
    rows.forEach(function (entry) {
      const item = document.createElement("li");
      const label = document.createElement("span");
      const value = document.createElement("strong");
      label.textContent = entry[0];
      value.textContent = entry[1];
      item.appendChild(label);
      item.appendChild(value);
      elements.statusList.appendChild(item);
    });
  }

  function appendLogs(entries) {
    if (!Array.isArray(entries) || entries.length === 0) {
      return;
    }

    entries.forEach(function (entry) {
      state.logs.push(entry);
    });

    if (state.logs.length > MAX_LINES) {
      state.logs.splice(0, state.logs.length - MAX_LINES);
    }

    renderLogs();
  }

  function appendCommandLogs(entries) {
    if (!Array.isArray(entries) || entries.length === 0) {
      return;
    }

    entries.forEach(function (entry) {
      state.commandLogs.push(entry);
    });

    if (state.commandLogs.length > MAX_LINES) {
      state.commandLogs.splice(0, state.commandLogs.length - MAX_LINES);
    }

    renderLogs();
  }

  function appendCommandActivity(text, level) {
    if (!text) {
      return;
    }

    state.commandLogs.push({
      text: text,
      message: text,
      level: level || "System"
    });

    if (state.commandLogs.length > MAX_LINES) {
      state.commandLogs.splice(0, state.commandLogs.length - MAX_LINES);
    }

    renderLogs();
  }

  function getLogEntryLevel(entry) {
    return entry ? (entry.level || entry.Level || "") : "";
  }

  function getLogEntryText(entry) {
    if (!entry) {
      return "";
    }

    return entry.text || entry.Text || entry.message || entry.Message || "";
  }

  function getLogEntrySequence(entry) {
    if (!entry) {
      return 0;
    }

    return Number(entry.sequence || entry.Sequence || 0);
  }

  function renderLogs() {
    elements.logSequenceLabel.textContent = "Sequence " + state.latestSequence;
    elements.terminalOutput.innerHTML = "";
    elements.commandOutput.innerHTML = "";

    if (state.logs.length === 0) {
      const empty = document.createElement("div");
      empty.className = "empty-state";
      empty.innerHTML = "<div><h3>No log lines yet</h3><p>Run a command or wait for the server to emit activity.</p></div>";
      elements.terminalOutput.appendChild(empty);
    } else {
      const fragment = document.createDocumentFragment();
      state.logs.forEach(function (entry) {
        const line = document.createElement("div");
        line.className = "terminal-line" + (getLogEntryLevel(entry) === "System" ? " system" : "");
        line.textContent = getLogEntryText(entry);
        fragment.appendChild(line);
      });

      elements.terminalOutput.appendChild(fragment);
      elements.terminalOutput.scrollTop = elements.terminalOutput.scrollHeight;
    }

    if (state.commandLogs.length === 0) {
      const commandEmpty = document.createElement("div");
      commandEmpty.className = "empty-state";
      commandEmpty.innerHTML = "<div><h3>No issued command output yet</h3><p>Run a remote command and its submission/result stream will appear here.</p></div>";
      elements.commandOutput.appendChild(commandEmpty);
    } else {
      const commandFragment = document.createDocumentFragment();
      state.commandLogs.forEach(function (entry) {
        const line = document.createElement("div");
        line.className = "terminal-line" + (getLogEntryLevel(entry) === "System" ? " system" : "");
        line.textContent = getLogEntryText(entry);
        commandFragment.appendChild(line);
      });
      elements.commandOutput.appendChild(commandFragment);
      elements.commandOutput.scrollTop = elements.commandOutput.scrollHeight;
    }
  }

  function setActiveTerminalTab(tabName) {
    state.activeTerminalTab = tabName === "command" ? "command" : "standard";
    elements.terminalOutput.classList.toggle("hidden", state.activeTerminalTab !== "standard");
    elements.commandOutput.classList.toggle("hidden", state.activeTerminalTab !== "command");
    elements.terminalTabs.forEach(function (button) {
      button.classList.toggle("active", button.getAttribute("data-terminal-tab") === state.activeTerminalTab);
    });
  }

  async function refreshSession() {
    const result = await apiRequest("/RemoteConsole/Session", {
      method: "GET"
    });

    const data = result.data || {};
    state.authenticated = !!data.authenticated;
    state.whitelisted = data.whitelisted !== false;
    state.session = data.session || null;
    if (!state.authenticated) {
      storeToken("");
    }
    updateAuthUi();

    if (!state.whitelisted) {
      setMessage(elements.loginMessage, data.message || "This IP address is not whitelisted for remote console login.", "error");
    } else if (!state.authenticated && !elements.loginMessage.textContent) {
      setMessage(elements.loginMessage, "Sign in with an admin account email and password.", "");
    }

    if (!state.authenticated && state.whitelisted) {
      await tryAccountPortalAutoLogin();
    }

    return data;
  }

  async function poll() {
    if (state.inFlight || !state.authenticated) {
      return;
    }

    state.inFlight = true;
    try {
      const result = await apiRequest("/RemoteConsole/Poll?since=" + encodeURIComponent(String(state.latestSequence)), {
        method: "GET"
      });

      if (result.status === 401) {
        state.authenticated = false;
        state.session = null;
        updateAuthUi();
        stopPolling();
        setMessage(elements.commandMessage, "Your session expired. Please sign in again.", "error");
        return;
      }

      if (!result.ok || !result.data) {
        return;
      }

      updateStatus(result.data.status);
      appendLogs(result.data.logs);
      state.latestSequence = Number(result.data.latestSequence || state.latestSequence);
    } catch (error) {
    } finally {
      state.inFlight = false;
    }
  }

  function startPolling() {
    stopPolling();
    poll();
    state.pollTimer = window.setInterval(poll, POLL_INTERVAL_MS);
  }

  function stopPolling() {
    if (state.pollTimer != null) {
      window.clearInterval(state.pollTimer);
      state.pollTimer = null;
    }
  }

  function startNavbarStatusPolling() {
    if (state.navbarStatusTimer != null) {
      window.clearInterval(state.navbarStatusTimer);
    }

    refreshNavbarServerStatus();
    state.navbarStatusTimer = window.setInterval(refreshNavbarServerStatus, NAVBAR_STATUS_POLL_INTERVAL_MS);
  }

  async function handleLogin(event) {
    event.preventDefault();
    setMessage(elements.loginMessage, "", "");

    const username = elements.username.value.trim();
    const password = elements.password.value;
    if (!username || !password) {
      setMessage(elements.loginMessage, "Enter both account email and password.", "error");
      return;
    }

    elements.loginSubmit.disabled = true;
    try {
      const result = await apiRequest("/RemoteConsole/Login", {
        method: "POST",
        body: JSON.stringify({
          Username: username,
          Password: password
        })
      });

      if (!result.ok || !result.data || !result.data.authenticated) {
        state.whitelisted = result.data ? result.data.whitelisted !== false : state.whitelisted;
        storeToken("");
        updateAuthUi();
        setMessage(elements.loginMessage, (result.data && result.data.message) || "Authentication failed.", "error");
        return;
      }

      storeToken(result.data.token || "");
      state.authenticated = true;
      state.whitelisted = true;
      state.session = result.data.session || null;
      state.logs = [];
      state.commandLogs = [];
      state.latestSequence = 0;
      updateAuthUi();
      setMessage(elements.loginMessage, result.data.message || "Authenticated.", "success");
      setMessage(elements.commandMessage, "Remote console unlocked.", "success");
      startPolling();
      elements.password.value = "";
      elements.commandInput.focus();
    } catch (error) {
      setMessage(elements.loginMessage, "Unable to reach the remote console backend.", "error");
    } finally {
      elements.loginSubmit.disabled = !state.whitelisted;
    }
  }

  async function handleLogout() {
    await apiRequest("/RemoteConsole/Logout", {
      method: "POST",
      body: "{}"
    });

    stopPolling();
    storeToken("");
    state.authenticated = false;
    state.session = null;
    state.logs = [];
    state.commandLogs = [];
    state.latestSequence = 0;
    renderLogs();
    updateAuthUi();
    setMessage(elements.commandMessage, "", "");
    setMessage(elements.loginMessage, "Signed out.", "success");
  }

  async function runCommand(command) {
    const trimmed = (command || "").trim();
    if (!trimmed) {
      setMessage(elements.commandMessage, "Enter a command to run.", "error");
      return;
    }

    if (!state.authenticated) {
      setMessage(elements.commandMessage, "Sign in before running commands.", "error");
      return;
    }

    elements.commandSubmit.disabled = true;
    try {
      const result = await apiRequest("/RemoteConsole/Command", {
        method: "POST",
        body: JSON.stringify({
          Command: trimmed
        })
      });

      if (result.status === 401) {
        await handleLogout();
        setMessage(elements.commandMessage, "Session expired. Please sign in again.", "error");
        return;
      }

      if (!result.ok) {
        appendCommandActivity("> " + trimmed, "System");
        appendCommandActivity((result.data && result.data.message) || "Command failed.", "Error");
        setActiveTerminalTab("command");
        setMessage(elements.commandMessage, (result.data && result.data.message) || "Command failed.", "error");
        return;
      }

      appendCommandActivity("> " + trimmed, "System");
      appendCommandActivity((result.data && result.data.message) || "Command submitted.", "System");
      if (result.data && result.data.output) {
        result.data.output.split(/\r?\n/).forEach(function (line) {
          if (line) {
            appendCommandActivity(line, "System");
          }
        });
      }
      setActiveTerminalTab("command");
      setMessage(elements.commandMessage, (result.data && result.data.message) || "Command submitted.", "success");
      elements.commandInput.value = "";
      await poll();
    } catch (error) {
      appendCommandActivity("> " + trimmed, "System");
      appendCommandActivity("Unable to submit command.", "Error");
      setActiveTerminalTab("command");
      setMessage(elements.commandMessage, "Unable to submit command.", "error");
    } finally {
      elements.commandSubmit.disabled = !state.authenticated;
    }
  }

  function applyTheme(theme) {
    document.documentElement.setAttribute("data-theme", theme);
    document.documentElement.style.colorScheme = theme;
    applyThemeLabel(theme);
    try {
      localStorage.setItem("coa-theme", theme);
    } catch (error) {}
  }

  function toggleTheme() {
    const current = document.documentElement.getAttribute("data-theme") === "light" ? "light" : "dark";
    applyTheme(current === "light" ? "dark" : "light");
  }

  function bindQuickActions() {
    document.querySelectorAll("[data-command]").forEach(function (button) {
      button.addEventListener("click", function () {
        const command = button.getAttribute("data-command") || "";
        elements.commandInput.value = command;
        if (state.authenticated) {
          runCommand(command);
        } else {
          elements.commandInput.focus();
          setMessage(elements.commandMessage, "Command staged. Sign in to run it.", "");
        }
      });
    });
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

  async function initialize() {
    elements.loginForm.addEventListener("submit", handleLogin);
    elements.loginClear.addEventListener("click", function () {
      elements.username.value = "";
      elements.password.value = "";
      setMessage(elements.loginMessage, "", "");
    });
    elements.logoutButton.addEventListener("click", handleLogout);
    elements.commandForm.addEventListener("submit", function (event) {
      event.preventDefault();
      runCommand(elements.commandInput.value);
    });
    elements.themeToggle.addEventListener("click", toggleTheme);
    elements.terminalTabs.forEach(function (button) {
      button.addEventListener("click", function () {
        setActiveTerminalTab(button.getAttribute("data-terminal-tab"));
      });
    });
    if (elements.hamburger) {
      elements.hamburger.addEventListener("click", function () {
        const expanded = elements.hamburger.getAttribute("aria-expanded") === "true";
        setNavbarExpanded(!expanded);
      });
    }
    elements.navLinks.forEach(function (link) {
      link.addEventListener("click", function () {
        setNavbarExpanded(false);
      });
    });
    window.addEventListener("resize", function () {
      if (window.innerWidth > 991) {
        setNavbarExpanded(false);
      }
    });
    window.addEventListener("scroll", handleScrollState, { passive: true });
    initializeServerDetailsModal();
    bindQuickActions();
    renderLogs();
    setActiveTerminalTab(state.activeTerminalTab);
    updateAuthUi();
    handleScrollState();
    startNavbarStatusPolling();

    const session = await refreshSession();
    if (session && session.authenticated) {
      setMessage(elements.loginMessage, "Existing session restored.", "success");
      startPolling();
    }
  }

  initialize();
}());
