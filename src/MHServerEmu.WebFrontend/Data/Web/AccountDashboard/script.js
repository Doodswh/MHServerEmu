(function () {
    const HERO_PORTRAITS = {
        angela: "drop_angela.png",
        antman: "drop_antman.png",
        beast: "drop_beast.png",
        blackbolt: "drop_blackbolt.png",
        blackcat: "drop_blackcat.png",
        blackpanther: "blackpanther.png",
        blackwidow: "blackwidow.png",
        blade: "drop_blade.png",
        cable: "cable.png",
        captainamerica: "captainamerica.png",
        captainmarvel: "drop_captainmarvel.png",
        carnage: "drop_carnage.png",
        colossus: "colossus.png",
        cyclops: "cyclops.png",
        daredevil: "daredevil.png",
        deadpool: "deadpool.png",
        doctordoom: "drdoom_ff.png",
        doctorstrange: "drstrange.png",
        drdoom: "drdoom_ff.png",
        drdoomff: "drdoom_ff.png",
        drstrange: "drstrange.png",
        elektra: "drop_elektra.png",
        emmafrost: "emmafrost.png",
        gambit: "gambit.png",
        ghostrider: "ghostrider.png",
        greengoblin: "drop_greengoblin.png",
        hawkeye: "hawkeye.png",
        hulk: "hulk.png",
        humantorch: "humantorch.png",
        iceman: "iceman.png",
        invisiblewoman: "invisiblewoman.png",
        ironfist: "drop_ironfist.png",
        ironman: "ironman.png",
        jeangrey: "jeangrey.png",
        juggernaut: "juggernaut.png",
        kittypryde: "drop_kittypryde.png",
        loki: "loki.png",
        lukecage: "lukecage.png",
        magik: "drop_magik.png",
        magneto: "magneto.png",
        misterfantastic: "mrfantastic.png",
        moonknight: "moonknight.png",
        mrfantastic: "mrfantastic.png",
        nickfury: "drop_nickfury.png",
        nightcrawler: "nightcrawler.png",
        nova: "nova.png",
        psylocke: "psylocke.png",
        punisher: "punisher.png",
        rocketraccoon: "rocketraccoon.png",
        rogue: "rogue.png",
        scarletwitch: "scarletwitch.png",
        shehulk: "shehulk.png",
        silversurfer: "silversurfer.png",
        spiderman: "spiderman.png",
        squirrel: "squirrel.png",
        squirrelgirl: "squirrel.png",
        starlord: "starlord.png",
        storm: "storm.png",
        taskmaster: "taskmaster.png",
        thing: "thing.png",
        thor: "thor.png",
        ultron: "drop_ultron.png",
        venom: "venom.png",
        vision: "drop_vision.png",
        warmachine: "drop_warmachine.png",
        wintersoldier: "wintersoldier.png",
        wolverine: "wolverine.png",
        x23: "x23.png"
    };

    function readStoredToken(key) {
        return sessionStorage.getItem(key) || localStorage.getItem(key) || "";
    }

    function writeStoredToken(key, value) {
        if (value) {
            sessionStorage.setItem(key, value);
            localStorage.setItem(key, value);
        } else {
            sessionStorage.removeItem(key);
            localStorage.removeItem(key);
        }
    }

    const state = {
        token: readStoredToken(dashboardConfig.tokenStorageKey) || "",
        loading: false,
        characterInventoryLoading: false
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
        serverMetricsLink: document.getElementById("server-metrics-link"),
        loginMessage: document.getElementById("login-message"),
        sessionState: document.getElementById("session-state"),
        sessionDetail: document.getElementById("session-detail"),
        accountOverview: document.getElementById("account-overview"),
        headlineStats: document.getElementById("headline-stats"),
        characterRows: document.getElementById("character-rows"),
        characterInventoryModal: document.getElementById("character-inventory-modal"),
        characterInventoryTitle: document.getElementById("character-inventory-title"),
        characterInventoryEyebrow: document.getElementById("character-inventory-eyebrow"),
        characterInventorySubtitle: document.getElementById("character-inventory-subtitle"),
        characterInventoryPortrait: document.getElementById("character-inventory-portrait"),
        characterEquipmentGrid: document.getElementById("character-equipment-grid"),
        characterCarriedGroups: document.getElementById("character-carried-groups"),
        stashesPanel: document.getElementById("stashes-panel"),
        stashesContainer: document.getElementById("stashes-container")
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
        writeStoredToken(dashboardConfig.tokenStorageKey, state.token);
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

    function navigateToProtectedAdminPage(path) {
        if (!state.token) {
            window.location.href = path;
            return;
        }

        const form = document.createElement("form");
        form.method = "post";
        form.action = path;
        form.style.display = "none";

        const tokenField = document.createElement("input");
        tokenField.type = "hidden";
        tokenField.name = "token";
        tokenField.value = state.token;
        form.appendChild(tokenField);

        document.body.appendChild(form);
        form.submit();
        form.remove();
    }

    function getServerMetricsHref() {
        return "/ServerMetrics/";
    }

    function getRemoteConsoleHref() {
        return "/COA-Dashboard/";
    }

    function showSignedOutView() {
        elements.authPanel.classList.remove("hidden");
        elements.dashboard.classList.add("hidden");
        elements.accountOverview.innerHTML = "";
        elements.headlineStats.innerHTML = "";
        elements.characterRows.innerHTML = "";
        if (elements.stashesContainer) elements.stashesContainer.innerHTML = "";
        if (elements.stashesPanel) elements.stashesPanel.style.display = "none";
        elements.coaDashboardLink.classList.add("hidden");
        elements.serverMetricsLink.classList.add("hidden");
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

    function getHeroPortraitBaseUrl() {
        return window.location.origin + "/AccountDashboard/hero_img/";
    }

    function getDisplayHeroName(name) {
        const normalized = normalizeHeroKey(name);
        if (normalized === "msmarvel") {
            return "Captain Marvel";
        }

        return formatValue(name);
    }

    function normalizeHeroKey(name) {
        return String(name || "")
            .toLowerCase()
            .replace(/[^a-z0-9]/g, "");
    }

    function getHeroPortraitUrl(name) {
        const portraitFile = HERO_PORTRAITS[normalizeHeroKey(name)];
        return portraitFile ? getHeroPortraitBaseUrl() + portraitFile : "";
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

        let characterInventoryBackdrop = null;

    function closeCharacterInventoryModal() {
        const modal = elements.characterInventoryModal;
        if (!modal) {
            return;
        }

        modal.classList.remove("show");
        modal.style.display = "none";
        modal.setAttribute("aria-hidden", "true");
        document.body.classList.remove("modal-open");

        if (characterInventoryBackdrop) {
            characterInventoryBackdrop.remove();
            characterInventoryBackdrop = null;
        }
    }

    function openCharacterInventoryModal() {
        const modal = elements.characterInventoryModal;
        if (!modal) {
            return;
        }

        modal.classList.add("show");
        modal.style.display = "block";
        modal.setAttribute("aria-hidden", "false");
        document.body.classList.add("modal-open");

        if (!characterInventoryBackdrop) {
            characterInventoryBackdrop = document.createElement("div");
            characterInventoryBackdrop.className = "modal-backdrop fade show";
            document.body.appendChild(characterInventoryBackdrop);
        }
    }

    function initializeCharacterInventoryModal() {
        const modal = elements.characterInventoryModal;
        if (!modal) {
            return;
        }

        modal.querySelectorAll("[data-dismiss='modal']").forEach(function (button) {
            button.addEventListener("click", function () {
                closeCharacterInventoryModal();
            });
        });

        modal.addEventListener("click", function (event) {
            if (event.target === modal) {
                closeCharacterInventoryModal();
            }
        });

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape" && modal.classList.contains("show")) {
                closeCharacterInventoryModal();
            }
        });
    }

    function renderCharacterInventoryPortrait(name) {
        const portraitUrl = getHeroPortraitUrl(name);
        elements.characterInventoryPortrait.innerHTML = "";

        if (!portraitUrl) {
            const fallback = document.createElement("span");
            fallback.className = "character-modal-portrait-fallback";
            fallback.textContent = "?";
            elements.characterInventoryPortrait.appendChild(fallback);
            return;
        }

        const image = document.createElement("img");
        image.className = "character-modal-portrait-image";
        image.src = portraitUrl;
        image.alt = name + " portrait";
        image.decoding = "async";
        image.loading = "eager";
        image.onerror = function () {
            elements.characterInventoryPortrait.innerHTML = '<span class="character-modal-portrait-fallback">?</span>';
        };
        elements.characterInventoryPortrait.appendChild(image);
    }

    function renderInventoryEmptyState(container, message) {
        const emptyState = document.createElement("div");
        emptyState.className = "empty-state";
        emptyState.textContent = message;
        container.appendChild(emptyState);
    }

    function getItemData(rawItem) {
        if (!rawItem || typeof rawItem !== "object") {
            return null;
        }

        const affixes = Array.isArray(rawItem.affixes) ? rawItem.affixes : Array.isArray(rawItem.Affixes) ? rawItem.Affixes : [];
        return {
            slot: rawItem.slot ?? rawItem.Slot ?? null,
            slotLabel: rawItem.slotLabel || rawItem.SlotLabel || "",
            name: formatValue(rawItem.name || rawItem.Name),
            prototypeName: rawItem.prototypeName || rawItem.PrototypeName || "",
            stackCount: Number(rawItem.stackCount || rawItem.StackCount || 0),
            iconAssetName: rawItem.iconAssetName || rawItem.IconAssetName || "",
            iconUrl: rawItem.iconUrl || rawItem.IconUrl || "",
            rarityName: rawItem.rarityName || rawItem.RarityName || "",
            rarityTier: Number(rawItem.rarityTier || rawItem.RarityTier || 0),
            itemLevel: Number(rawItem.itemLevel || rawItem.ItemLevel || 0),
            requiredLevel: Number(rawItem.requiredLevel || rawItem.RequiredLevel || 0),
            description: rawItem.description || rawItem.Description || "",
            flavorText: rawItem.flavorText || rawItem.FlavorText || "",
            affixes: affixes.filter(Boolean),
            tooltipHtml: rawItem.tooltipHtml || rawItem.TooltipHtml || "",
            tooltipText: rawItem.tooltipText || rawItem.TooltipText || "",
            itemBaseTypeName: rawItem.itemBaseTypeName || rawItem.ItemBaseTypeName || "",
            itemBaseRequiredLevelText: rawItem.itemBaseRequiredLevelText || rawItem.ItemBaseRequiredLevelText || "",
            itemBaseItemGradeText: rawItem.itemBaseItemGradeText || rawItem.ItemBaseItemGradeText || ""
        };
    }

    function normalizeTooltipText(value) {
        return String(value || "")
            .replace(/\r\n/g, "\n")
            .replace(/\r/g, "\n")
            .replace(/[ \t]+\n/g, "\n")
            .trim();
    }

    function getItemIconUrl(item) {
        const directUrl = item && item.iconUrl ? String(item.iconUrl).trim() : "";
        if (directUrl) {
            if (/^https?:\/\//i.test(directUrl)) {
                return directUrl;
            }

            if (directUrl.startsWith("/")) {
                return window.location.origin + directUrl;
            }

            return window.location.origin + "/" + directUrl.replace(/^\/+/, "");
        }

        const assetName = item && item.iconAssetName ? String(item.iconAssetName).trim() : "";
        if (!assetName || !/\.(png|jpe?g|webp|gif|svg)$/i.test(assetName)) {
            return "";
        }

        if (/^https?:\/\//i.test(assetName)) {
            return assetName;
        }

        if (assetName.startsWith("/")) {
            return window.location.origin + assetName;
        }

        return window.location.origin + "/" + assetName.replace(/^\/+/, "");
    }

    function getItemInitials(name) {
        const cleaned = String(name || "")
            .replace(/[^A-Za-z0-9 ]+/g, " ")
            .trim();

        if (!cleaned) {
            return "?";
        }

        const words = cleaned.split(/\s+/).filter(Boolean);
        if (words.length === 1) {
            return words[0].slice(0, 2).toUpperCase();
        }

        return (words[0][0] + words[1][0]).toUpperCase();
    }

    function getItemRarityClass(item) {
        const tier = Math.max(0, Math.min(6, Number(item && item.rarityTier ? item.rarityTier : 0)));
        return tier > 0 ? "rarity-tier-" + tier : "rarity-tier-0";
    }

    function buildItemBaseTooltipContent(item) {
        const tooltipHtml = String(item && item.tooltipHtml ? item.tooltipHtml : "").trim();
        if (!tooltipHtml) {
            return null;
        }

        const wrapper = document.createElement("div");
        wrapper.className = "itembase-tooltip-content";
        wrapper.innerHTML = tooltipHtml;
        return wrapper;
    }

    function buildInventorySlotTooltip(item) {
        if (!item) {
            return null;
        }

        const tooltip = document.createElement("aside");
        tooltip.className = "inventory-slot-tooltip " + getItemRarityClass(item);

        const itemBaseTooltip = buildItemBaseTooltipContent(item);
        if (itemBaseTooltip) {
            tooltip.classList.add("inventory-slot-tooltip-itembase");
            tooltip.appendChild(itemBaseTooltip);
            return tooltip;
        }

        const header = document.createElement("div");
        header.className = "inventory-slot-tooltip-head";
        header.innerHTML = '<div class="inventory-slot-tooltip-title-wrap"><strong class="inventory-slot-tooltip-title"></strong><span class="inventory-slot-tooltip-rarity"></span></div><div class="inventory-slot-tooltip-meta"></div>';
        header.querySelector(".inventory-slot-tooltip-title").textContent = item.name;
        header.querySelector(".inventory-slot-tooltip-rarity").textContent = item.rarityName || "Item";

        const metaParts = [];
        if (item.slotLabel) {
            metaParts.push(item.slotLabel);
        }
        if (item.itemLevel > 0) {
            metaParts.push("Item Lv. " + item.itemLevel);
        }
        if (item.requiredLevel > 0) {
            metaParts.push("Req. Lv. " + item.requiredLevel);
        }
        if (item.stackCount > 1) {
            metaParts.push("Stack x" + item.stackCount);
        }
        header.querySelector(".inventory-slot-tooltip-meta").textContent = metaParts.join(" · ");
        tooltip.appendChild(header);

        const descriptionText = normalizeTooltipText(item.description);
        if (descriptionText) {
            const description = document.createElement("p");
            description.className = "inventory-slot-tooltip-description";
            description.textContent = descriptionText;
            tooltip.appendChild(description);
        }

        if (item.affixes.length) {
            const statBlock = document.createElement("div");
            statBlock.className = "inventory-slot-tooltip-block";

            const statLabel = document.createElement("span");
            statLabel.className = "inventory-slot-tooltip-label";
            statLabel.textContent = "Stats";
            statBlock.appendChild(statLabel);

            const statList = document.createElement("ul");
            statList.className = "inventory-slot-tooltip-list";
            item.affixes.forEach(function (affix) {
                const entry = document.createElement("li");
                entry.textContent = affix;
                statList.appendChild(entry);
            });

            statBlock.appendChild(statList);
            tooltip.appendChild(statBlock);
        }

        const flavorText = normalizeTooltipText(item.flavorText);
        if (flavorText) {
            const flavor = document.createElement("p");
            flavor.className = "inventory-slot-tooltip-flavor";
            flavor.textContent = flavorText;
            tooltip.appendChild(flavor);
        }

        return tooltip;
    }

    function buildInventorySlotCard(options) {
        const card = document.createElement("article");
        card.className = options.className || "inventory-slot-card";
        const item = getItemData(options.item);

        const label = document.createElement("span");
        label.className = "inventory-slot-label";
        label.textContent = options.label || "Slot";
        card.appendChild(label);

        if (item) {
            card.classList.add("has-tooltip");
            card.tabIndex = 0;

            const visual = document.createElement("div");
            visual.className = "inventory-slot-visual " + getItemRarityClass(item);

            const iconUrl = getItemIconUrl(item);
            if (iconUrl) {
                const image = document.createElement("img");
                image.className = "inventory-slot-icon";
                image.src = iconUrl;
                image.alt = item.name;
                image.loading = "lazy";
                image.decoding = "async";
                image.onerror = function () {
                    visual.innerHTML = '<span class="inventory-slot-icon-fallback">' + getItemInitials(item.name) + "</span>";
                };
                visual.appendChild(image);
            } else {
                visual.innerHTML = '<span class="inventory-slot-icon-fallback">' + getItemInitials(item.name) + "</span>";
            }

            card.appendChild(visual);
        }

        const title = document.createElement("strong");
        title.className = "inventory-slot-name";
        title.textContent = item ? item.name : options.title || "Empty";
        card.appendChild(title);

        if (item && item.name) {
            card.title = item.name;
        }

        if (item && item.stackCount > 1) {
            const badge = document.createElement("span");
            badge.className = "inventory-slot-badge";
            badge.textContent = "x" + item.stackCount;
            card.appendChild(badge);
        }

        if (item) {
            const tooltip = buildInventorySlotTooltip(item);
            if (tooltip) {
                card.appendChild(tooltip);
            }
        }

        return card;
    }

    function renderCharacterInventory(data) {
        const character = data.character || {};
        const equipment = Array.isArray(data.equipment) ? data.equipment : [];
        const carriedInventories = Array.isArray(data.carriedInventories) ? data.carriedInventories : [];
        const displayName = getDisplayHeroName(character.name || character.Name);
        const level = formatValue(character.level || character.Level, "0");
        const timePlayed = formatDuration(character.timePlayed || character.TimePlayed);

        elements.characterInventoryEyebrow.textContent = "Inventory Inspection";
        elements.characterInventoryTitle.textContent = displayName;
        elements.characterInventorySubtitle.textContent = "Level " + level + " · " + timePlayed + " played";
        renderCharacterInventoryPortrait(displayName);

        elements.characterEquipmentGrid.innerHTML = "";
        if (!equipment.length) {
            renderInventoryEmptyState(elements.characterEquipmentGrid, "No equipped items were found for this character.");
        } else {
            equipment.forEach(function (slot) {
                const item = slot.item || slot.Item;
                elements.characterEquipmentGrid.appendChild(buildInventorySlotCard({
                    className: "inventory-slot-card equipment-slot-card" + (item ? " is-filled" : " is-empty"),
                    label: formatValue(slot.slotLabel || slot.SlotLabel, "Equipment"),
                    title: item ? formatValue(item.name || item.Name) : "Empty",
                    item: item
                }));
            });
        }

        elements.characterCarriedGroups.innerHTML = "";
        if (!carriedInventories.length) {
            renderInventoryEmptyState(elements.characterCarriedGroups, "No carried inventory containers were available.");
            return;
        }

        carriedInventories.forEach(function (inventory) {
            const section = document.createElement("section");
            section.className = "inventory-group";

            const header = document.createElement("div");
            header.className = "inventory-group-head";
            header.innerHTML = '<div><p class="eyebrow modal-eyebrow"></p><h4></h4></div><span class="inventory-group-meta"></span>';
            header.querySelector(".eyebrow").textContent = "Bag";
            header.querySelector("h4").textContent = formatValue(inventory.inventoryLabel || inventory.InventoryLabel, "Inventory");
            header.querySelector(".inventory-group-meta").textContent = formatValue(inventory.itemCount || inventory.ItemCount, "0") + " / " + formatValue(inventory.capacity || inventory.Capacity, "0") + " slots used";
            section.appendChild(header);

            const grid = document.createElement("div");
            grid.className = "inventory-bag-grid";
            const items = Array.isArray(inventory.items) ? inventory.items : [];
            if (!items.length) {
                renderInventoryEmptyState(grid, "This inventory is empty.");
            } else {
                items.forEach(function (item) {
                    grid.appendChild(buildInventorySlotCard({
                        className: "inventory-slot-card bag-slot-card is-filled",
                        label: formatValue(item.slotLabel || item.SlotLabel, "Slot"),
                        title: formatValue(item.name || item.Name),
                        item: item
                    }));
                });
            }

            section.appendChild(grid);
            elements.characterCarriedGroups.appendChild(section);
        });
    }

    async function loadCharacterInventory(character) {
        const characterId = character.characterId || character.CharacterId;
        if (!characterId || state.characterInventoryLoading) {
            return;
        }

        state.characterInventoryLoading = true;
        openCharacterInventoryModal();
        elements.characterInventoryEyebrow.textContent = "Inventory Inspection";
        elements.characterInventoryTitle.textContent = getDisplayHeroName(character.name || character.Name);
        elements.characterInventorySubtitle.textContent = "Loading equipment and carried inventory...";
        renderCharacterInventoryPortrait(getDisplayHeroName(character.name || character.Name));
        elements.characterEquipmentGrid.innerHTML = "";
        elements.characterCarriedGroups.innerHTML = "";
        renderInventoryEmptyState(elements.characterEquipmentGrid, "Loading equipment...");
        renderInventoryEmptyState(elements.characterCarriedGroups, "Loading inventory...");

        try {
            const result = await request(dashboardConfig.endpoints.character, {
                method: "POST",
                body: JSON.stringify({ CharacterId: characterId })
            });

            if (!result.ok || !result.data) {
                if (result.status === 401) {
                    setToken("");
                    closeCharacterInventoryModal();
                    showSignedOutView();
                    setSessionState("Signed out", (result.data && result.data.message) || "No active account session was found.");
                    return;
                }

                throw new Error((result.data && result.data.message) || "Unable to load character inventory.");
            }

            renderCharacterInventory(result.data);
        } catch (error) {
            elements.characterInventoryTitle.textContent = getDisplayHeroName(character.name || character.Name);
            elements.characterInventorySubtitle.textContent = error.message || "Unable to load character inventory.";
            elements.characterEquipmentGrid.innerHTML = "";
            elements.characterCarriedGroups.innerHTML = "";
            renderInventoryEmptyState(elements.characterEquipmentGrid, "Unable to load equipment for this character.");
            renderInventoryEmptyState(elements.characterCarriedGroups, "Unable to load carried inventory for this character.");
        } finally {
            state.characterInventoryLoading = false;
        }
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
        elements.serverMetricsLink.classList.toggle("hidden", !isAdminUser(userLevel));
        elements.serverMetricsLink.href = getServerMetricsHref();

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
        const commendationsGrid = document.getElementById('commendations-grid');
        const commendationsPanel = document.getElementById('commendations-panel');
        const commendationsData = data.commendations || data.Commendations || [];

        if (commendationsGrid && commendationsData.length > 0) {
            let commendationsHtml = '';

            commendationsData.forEach(function (comm) {
                const count = comm.count || comm.Count || 0;
                const maxDrops = comm.maxDrops || comm.MaxDrops || 1;
                const remaining = comm.remaining || comm.Remaining || 0;
                const displayName = comm.displayName || comm.DisplayName || "Commendation";

                const percent = Math.max(0, Math.min(100, Math.round((count / maxDrops) * 100)));

                commendationsHtml += `
                    <div class="glass-inset" style="padding: 1rem; border-radius: 8px;">
                        <p class="eyebrow" style="margin-bottom: 5px;">${displayName}</p>
                        <div style="display: flex; justify-content: space-between; align-items: baseline;">
                            <h3 style="margin: 0;">${count} <span style="font-size: 0.6em; font-weight: normal; opacity: 0.7;">/ ${maxDrops}</span></h3>
                            <span style="font-size: 0.85rem; opacity: 0.8;">${remaining} remaining</span>
                        </div>
                        <div style="width: 100%; background-color: rgba(255, 255, 255, 0.1); border-radius: 4px; height: 10px; margin-top: 12px; overflow: hidden;">
                            <div style="height: 100%; width: ${percent}%; background-color: #986698; border-radius: 4px; transition: width 0.5s ease-in-out;"></div>
                        </div>
                    </div>
                `;
            });

            commendationsGrid.innerHTML = commendationsHtml;
            if (commendationsPanel) commendationsPanel.style.display = 'block';
        } else if (commendationsPanel) {
            commendationsPanel.style.display = 'none';
        }
        const stashesData = data.stashes || data.Stashes || [];
        if (elements.stashesContainer && elements.stashesPanel) {
            elements.stashesContainer.innerHTML = "";

            if (stashesData.length > 0) {
                elements.stashesPanel.style.display = "block";

                stashesData.forEach(function (stash) {
                    const section = document.createElement("section");
                    section.className = "inventory-group";

                    const header = document.createElement("div");
                    header.className = "inventory-group-head";
                    header.innerHTML = '<div><p class="eyebrow modal-eyebrow">Stash Tab</p><h4></h4></div><span class="inventory-group-meta"></span>';
                    header.querySelector("h4").textContent = formatValue(stash.inventoryLabel || stash.InventoryLabel, "Stash Tab");
                    header.querySelector(".inventory-group-meta").textContent = formatValue(stash.itemCount || stash.ItemCount, "0") + " / " + formatValue(stash.capacity || stash.Capacity, "0") + " slots used";
                    section.appendChild(header);

                    const grid = document.createElement("div");
                    grid.className = "inventory-bag-grid";
                    const items = Array.isArray(stash.items || stash.Items) ? (stash.items || stash.Items) : [];

                    if (!items.length) {
                        renderInventoryEmptyState(grid, "This stash tab is empty.");
                    } else {
                        items.forEach(function (item) {
                            grid.appendChild(buildInventorySlotCard({
                                className: "inventory-slot-card bag-slot-card is-filled",
                                label: formatValue(item.slotLabel || item.SlotLabel, "Slot"),
                                title: formatValue(item.name || item.Name),
                                item: item
                            }));
                        });
                    }

                    section.appendChild(grid);
                    elements.stashesContainer.appendChild(section);
                });
            } else {
                elements.stashesPanel.style.display = "none";
            }
        }
        if (!characters.length) {
            const row = document.createElement("tr");
            row.innerHTML = '<td colspan="4"><div class="empty-state">No saved characters were found for this account.</div></td>';
            elements.characterRows.appendChild(row);
            return;
        }

        characters.forEach(function (character) {
            const row = document.createElement("tr");
            row.className = "character-row-trigger";
            row.tabIndex = 0;
            row.innerHTML = [
                '<td class="character-portrait-cell" data-label=""></td>',
                '<td data-label="Character"></td>',
                '<td data-label="Level"></td>',
                '<td data-label="Time Played"></td>'
            ].join("");

            const cells = row.querySelectorAll("td");
            const characterName = getDisplayHeroName(character.name || character.Name);
            const portraitUrl = getHeroPortraitUrl(characterName);
            cells[0].innerHTML = portraitUrl
                ? '<span class="character-portrait-frame"><img class="character-portrait" src="' + portraitUrl + '" alt="' + characterName + ' portrait" loading="eager" decoding="async"></span>'
                : '<span class="character-portrait-fallback">?</span>';
            cells[1].textContent = characterName;
            cells[2].textContent = formatValue(character.level || character.Level, "0");
            cells[3].textContent = formatDuration(character.timePlayed || character.TimePlayed);
            row.addEventListener("click", function () {
                loadCharacterInventory(character);
            });
            row.addEventListener("keydown", function (event) {
                if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    loadCharacterInventory(character);
                }
            });
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
    initializeCharacterInventoryModal();

    elements.loginForm.addEventListener("submit", handleLogin);
    elements.clearButton.addEventListener("click", function () {
        elements.loginForm.reset();
        setMessage("", "");
    });
    elements.coaDashboardLink.addEventListener("click", function (event) {
        event.preventDefault();
        writeStoredToken(dashboardConfig.tokenStorageKey, state.token);
        navigateToProtectedAdminPage(getRemoteConsoleHref());
    });
    elements.serverMetricsLink.addEventListener("click", function (event) {
        event.preventDefault();
        navigateToProtectedAdminPage(getServerMetricsHref());
    });
    elements.logoutButton.addEventListener("click", handleLogout);

    restoreSession().catch(function (error) {
        setToken("");
        showSignedOutView();
        setSessionState("Awaiting sign-in", "Session restore failed.");
        setMessage(error.message || "Unable to restore the previous session.", "error");
    });
}());











