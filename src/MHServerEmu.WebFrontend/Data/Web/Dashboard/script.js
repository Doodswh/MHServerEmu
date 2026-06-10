if (dashboardConfig == null) {
	const dashboardConfig = {
		originSuffix: "",
	}
}

(function() {

const apiUtil = {
	handleReadyStateChange(xhr, callback) {
		if (xhr.readyState != 4)
			return;

		const response = xhr.status == 200 ? JSON.parse(xhr.responseText) : null;
		callback(response);
	},
	
	get(path, callback) {
		const url = window.location.origin + dashboardConfig.originSuffix + path;

		const xhr = new XMLHttpRequest();
		xhr.open("GET", url, true);
		xhr.onreadystatechange = () => this.handleReadyStateChange(xhr, callback);
		xhr.send();
	},

	post(path, data, callback) {
		const url = window.location.origin + dashboardConfig.originSuffix + path;
		const json = JSON.stringify(data);

		const xhr = new XMLHttpRequest();
		xhr.open("POST", url, true);
		xhr.onreadystatechange = () => this.handleReadyStateChange(xhr, callback);
		xhr.setRequestHeader("Content-Type", "application/json");
		xhr.send(json);
	},
}

const htmlUtil = {
	createAndAppendChild(parent, tagName, text = "") {
		var child = document.createElement(tagName);

		if (text != "") {
			const textNode = document.createTextNode(text);
			child.appendChild(textNode);
		}

		parent.appendChild(child);
		return child;
	},

	createAndAppendList(parent, listData, isOrdered = false) {
		const list = this.createAndAppendChild(parent, isOrdered ? "ol": "ul");

		for (let i = 0; i < listData.length; i++) {
			this.createAndAppendChild(list, "li", listData[i]);
		}

		return list;
	},

	createAndAppendTable(parent, tableData, useHeader = true) {
		const tableContainer = this.createAndAppendChild(parent, "div");
		tableContainer.className = "table-container";

		const table = this.createAndAppendChild(tableContainer, "table");

		for (let i = 0; i < tableData.length; i++) {
			const rowData = tableData[i];
			const row = this.createAndAppendChild(table, "tr");

			for (let j = 0; j < rowData.length; j++) {
				const cellData = rowData[j];
				const cellTag = useHeader && i == 0 ? "th": "td";
				this.createAndAppendChild(row, cellTag, cellData);
			}
		}

		return table;
	}
}

const stringUtil = {
	bigIntToHexString(value, upper = true) {
		let str = BigInt(value).toString(16);

		if (upper)
			str = str.toUpperCase();

		return str;
	},

	formatTimeDiff(timeMS)
	{
		const MS_PER_SECOND = 1000;
		const MS_PER_MINUTE = MS_PER_SECOND * 60;
		const MS_PER_HOUR = MS_PER_MINUTE * 60;

		const hours = Math.floor(timeMS / MS_PER_HOUR);
		const minutes = Math.floor((timeMS % MS_PER_HOUR) / MS_PER_MINUTE);
		const seconds = Math.floor((timeMS % MS_PER_MINUTE) / MS_PER_SECOND);

		return [
			hours.toString().padStart(2, '0'),
			minutes.toString().padStart(2, '0'),
			seconds.toString().padStart(2, '0'),
		].join(':');
	}
}

const tabManager = {
	currentTabId: "",

	initialize(tabs) {
		for (let i = 0; i < tabs.length; i++) {
			const tab = tabs[i];
			document.getElementById(tab.tabName + "-tab-button").onclick = () => this.openTab(tab);
			tab.initialize();
		}

		this.openTab(null);
	},

	openTab(tab) {
		const tabId = tab != null ? `${tab.tabName}-tab` : "";
		
		const tabs = document.getElementsByClassName("tab-content");
		for (let i = 0; i < tabs.length; i++) {
			tabs[i].style.display = "none";
		}
		
		if (tabId == "") {
			return;
		}
		else if (tabId == this.currentTabId) {
			this.currentTabId = "";
			return;
		}

		this.currentTabId = tabId;
		document.getElementById(tabId).style.display = "block";
	},
}

const serverStatusTab = {
	tabName: "server-status",

	initialize() {
		document.getElementById("server-status-button").onclick = () => this.requestData();
	},

	requestData() {
		apiUtil.get("/ServerStatus", (data) => this.onDataReceived(data));
	},

	onDataReceived(data) {
		if (data == null)
			return;

		const serverStatusContainer = document.getElementById("server-status-container");
		serverStatusContainer.innerHTML = "";

		const listData = [
			`Uptime: ${stringUtil.formatTimeDiff((data.CurrentTime - data.StartupTime) * 1000)}`,
			`[GameInstance] Games: ${data.GisGames} | Players: ${data.GisPlayers}`,
			`[Leaderboard] Leaderboards: ${data.Leaderboards}`,
			`[PlayerManager] Games: ${data.PlayerManagerGames} | Players: ${data.PlayerManagerPlayers} | Sessions: ${data.PlayerManagerActiveSessions} [${data.PlayerManagerPendingSessions}]`,
			`[GroupingManager] Players: ${data.GroupingManagerPlayers}`,
			//`[Billing]`,
			`[Frontend] Connections: ${data.FrontendConnections} | Clients: ${data.FrontendClients}`,
			`[Auth] Handlers: ${data.WebFrontendHandlers} | Handled Requests: ${data.WebFrontendHandledRequests}`,
		];

		htmlUtil.createAndAppendList(serverStatusContainer, listData);
	},

	
}

const metricsTab = {
	tabName: "metrics",
	regionReportsByGame: {},

	initialize() {
		document.getElementById("metrics-button").onclick = () => this.requestData();
		document.getElementById("metrics-game-select").onchange = () => this.onGameMetricSelectChanged();
		document.getElementById("metrics-region-game-select").onchange = () => this.onRegionGameSelectChanged();
	},

	requestData() {
		apiUtil.get("/Metrics/Performance", (data) => this.onDataReceived(data));
	},

	onDataReceived(data) {
		if (data == null)
			return;

		this.updateReportMetadata(data);
		this.updateMemoryMetrics(data.Memory);
		this.updateGameMetrics(data.Games);
		this.updateRegionMetrics(data.RegionsByGame || {});
	},

	onGameMetricSelectChanged() {
		const select = document.getElementById("metrics-game-select");
		const metric = select.selectedIndex >= 0 ? select.options[select.selectedIndex].value : "";
		this.selectGameMetric(metric);
	},

	onRegionGameSelectChanged() {
		const select = document.getElementById("metrics-region-game-select");
		const gameId = select.selectedIndex >= 0 ? select.options[select.selectedIndex].value : "";
		this.selectRegionGame(gameId);
	},

	updateReportMetadata(data) {
		const metadataContainer = document.getElementById("metrics-report-metadata");
		metadataContainer.innerHTML = "";

		htmlUtil.createAndAppendChild(metadataContainer, "p", `Report 0x${stringUtil.bigIntToHexString(data.Id)}`);
	},

	updateMemoryMetrics(data) {
		const memoryContainer = document.getElementById("metrics-memory-container");
		memoryContainer.innerHTML = "";
		
		const listData = [
			`GCIndex: ${data.GCIndex}`,
			`GCCounts: Gen0=${data.GCCountGen0}, Gen1=${data.GCCountGen1}, Gen2=${data.GCCountGen2}`,
			`HeapSizeBytes: ${data.HeapSizeBytes.toLocaleString()} / ${data.TotalCommittedBytes.toLocaleString()}`,
			`PauseTimePercentage: ${data.PauseTimePercentage}%`,
			`PauseDuration: ${this.formatTracker(data.PauseDuration)}`,
		];

		htmlUtil.createAndAppendList(memoryContainer, listData);
	},

	updateGameMetrics(data) {
		const gameMetricSelect = document.getElementById("metrics-game-select");
		let prevSelectedIndex = gameMetricSelect.selectedIndex;
		gameMetricSelect.innerHTML = "";

		const gameMetricContainer = document.getElementById("metrics-game-container");
		gameMetricContainer.innerHTML = "";

		const dataMap = new Map();

		for (const key in data) {
			const entry = data[key];

			for (const metric in entry) {
				const value = entry[metric];

				if (dataMap.has(metric) == false)
					dataMap.set(metric, [["GameId", "Avg", "Mdn", "Last", "Min", "Max"]]);

				dataMap.get(metric).push([
					`0x${stringUtil.bigIntToHexString(key)}`,
					value.Average.toFixed(2),
					value.Median.toFixed(2),
					value.Last.toFixed(2),
					value.Min.toFixed(2),
					value.Max.toFixed(2),
				]);
			}
		}
		
		dataMap.forEach((value, key) => {
			const selectOption = htmlUtil.createAndAppendChild(gameMetricSelect, "option", key);
			selectOption.value = key;

			const subcontainer = htmlUtil.createAndAppendChild(gameMetricContainer, "div");
			subcontainer.id = `${key}-subcontainer`;
			subcontainer.className = "game-metric-subcontainer";

			htmlUtil.createAndAppendTable(subcontainer, value);
		});

		if (dataMap.size > 0) {
			if (prevSelectedIndex == -1)
				prevSelectedIndex = 0;

			gameMetricSelect.selectedIndex = Math.min(prevSelectedIndex, gameMetricSelect.options.length - 1);
			this.onGameMetricSelectChanged();
		}
	},

	updateRegionMetrics(data) {
		this.regionReportsByGame = data;

		const regionGameSelect = document.getElementById("metrics-region-game-select");
		const previousGameId = regionGameSelect.value;
		regionGameSelect.innerHTML = "";

		const regionContainer = document.getElementById("metrics-regions-container");
		const regionSummary = document.getElementById("metrics-regions-summary");
		regionContainer.innerHTML = "";
		regionSummary.innerHTML = "";

		const gameIds = Object.keys(data).sort((left, right) => {
			const leftId = BigInt(left);
			const rightId = BigInt(right);
			return leftId < rightId ? -1 : (leftId > rightId ? 1 : 0);
		});

		if (gameIds.length === 0) {
			htmlUtil.createAndAppendChild(regionContainer, "p", "No region scheduler data available.");
			return;
		}

		for (let i = 0; i < gameIds.length; i++) {
			const gameId = gameIds[i];
			const reports = data[gameId] || [];
			const option = htmlUtil.createAndAppendChild(regionGameSelect, "option", `Game 0x${stringUtil.bigIntToHexString(gameId)} (${reports.length} regions)`);
			option.value = gameId;
		}

		regionGameSelect.value = gameIds.includes(previousGameId) ? previousGameId : gameIds[0];
		this.onRegionGameSelectChanged();
	},

	selectGameMetric(metric) {
		const metricSubcontainerId = metric != "" ? `${metric}-subcontainer` : "";
		
		const subcontainers = document.getElementsByClassName("game-metric-subcontainer");
		for (let i = 0; i < subcontainers.length; i++)
			subcontainers[i].style.display = "none";
		
		if (metricSubcontainerId == "")
			return;

		document.getElementById(metricSubcontainerId).style.display = "block";
	},

	selectRegionGame(gameId) {
		const regionContainer = document.getElementById("metrics-regions-container");
		const regionSummary = document.getElementById("metrics-regions-summary");
		regionContainer.innerHTML = "";
		regionSummary.innerHTML = "";

		if (gameId == "")
			return;

		const reports = (this.regionReportsByGame[gameId] || []).slice().sort((left, right) => {
			const leftScore = this.getHotnessScore(left);
			const rightScore = this.getHotnessScore(right);
			if (leftScore !== rightScore)
				return rightScore - leftScore;

			const leftPending = left.Aoi.Pending + left.Events.Pending + left.Transfers.Pending;
			const rightPending = right.Aoi.Pending + right.Events.Pending + right.Transfers.Pending;
			if (leftPending !== rightPending)
				return rightPending - leftPending;

			return Number(BigInt(left.RegionId) - BigInt(right.RegionId));
		});

		this.renderRegionSummary(regionSummary, reports);

		const tableData = [["RegionId", "Prototype", "Players", "Match", "Transfers", "AOI", "Events"]];
		for (let i = 0; i < reports.length; i++) {
			const report = reports[i];
			tableData.push([
				`0x${stringUtil.bigIntToHexString(report.RegionId)}`,
				report.PrototypeName,
				report.PlayerCount.toString(),
				report.MatchNumber.toString(),
				this.formatRegionPhase(report.Transfers),
				this.formatRegionPhase(report.Aoi),
				this.formatRegionPhase(report.Events),
			]);
		}

		htmlUtil.createAndAppendTable(regionContainer, tableData);
	},

	renderRegionSummary(parent, reports) {
		const cards = htmlUtil.createAndAppendChild(parent, "div");
		cards.className = "metrics-region-summary-grid";

		const hotRegions = reports.filter((report) => report.Transfers.IsHot || report.Aoi.IsHot || report.Events.IsHot).length;
		const totalPlayers = reports.reduce((sum, report) => sum + report.PlayerCount, 0);
		const busiestRegion = reports.reduce((best, report) => {
			if (best == null)
				return report;
			return this.getHotnessScore(report) > this.getHotnessScore(best) ? report : best;
		}, null);
		const hottestLabel = busiestRegion != null
			? `${busiestRegion.PrototypeName} (0x${stringUtil.bigIntToHexString(busiestRegion.RegionId)})`
			: "n/a";
		const hottestPressure = busiestRegion != null
			? `${this.formatRegionPhaseShort(busiestRegion.Aoi)} | ${this.formatRegionPhaseShort(busiestRegion.Events)}`
			: "n/a";

		this.appendSummaryCard(cards, "Regions", reports.length.toString(), "Visible regions in this game report");
		this.appendSummaryCard(cards, "Hot Regions", hotRegions.toString(), "Regions currently flagged hot in any phase");
		this.appendSummaryCard(cards, "Players", totalPlayers.toString(), "Players across the reported regions");
		this.appendSummaryCard(cards, "Top Hotspot", hottestLabel, hottestPressure);
	},

	appendSummaryCard(parent, label, value, detail) {
		const card = htmlUtil.createAndAppendChild(parent, "div");
		card.className = "metrics-region-summary-card";

		const labelNode = htmlUtil.createAndAppendChild(card, "div", label);
		labelNode.className = "metrics-region-summary-label";

		const valueNode = htmlUtil.createAndAppendChild(card, "div", value);
		valueNode.className = "metrics-region-summary-value";

		const detailNode = htmlUtil.createAndAppendChild(card, "div", detail);
		detailNode.className = "metrics-region-summary-detail";
	},

	getHotnessScore(report) {
		let score = 0;
		score += report.Transfers.IsHot ? 1 : 0;
		score += report.Aoi.IsHot ? 2 : 0;
		score += report.Events.IsHot ? 2 : 0;
		score += report.Transfers.Pending + report.Aoi.Pending + report.Events.Pending;
		return score;
	},

	formatRegionPhase(phase) {
		const status = phase.IsHot ? "HOT" : "OK";
		return `${status} | pending=${phase.Pending} | avg=${phase.AverageElapsedMilliseconds.toFixed(2)} ms | last=${phase.LastElapsedMilliseconds.toFixed(2)} ms | budget=${phase.LastBudget} | processed=${phase.LastProcessed}`;
	},

	formatRegionPhaseShort(phase) {
		const status = phase.IsHot ? "HOT" : "OK";
		return `${status}, pending=${phase.Pending}, avg=${phase.AverageElapsedMilliseconds.toFixed(2)} ms`;
	},

	formatTracker(tracker) {
		return `avg=${tracker.Average.toFixed(2)}, mdn=${tracker.Median.toFixed(2)}, last=${tracker.Last.toFixed(2)}, min=${tracker.Min.toFixed(2)}, max=${tracker.Max.toFixed(2)}`;
	}
}

const regionReportTab = {
	tabName: "region-report",

	initialize() {
		document.getElementById("region-report-button").onclick = () => this.requestData();	
	},

	requestData() {
		apiUtil.get("/RegionReport", (data) => this.onDataReceived(data));
	},

	onDataReceived(data) {
		if (data == null)
			return;

		const regionReportContainer = document.getElementById("region-report-container");
		regionReportContainer.innerHTML = "";

		const tableData = [["GameId", "RegionId", "Name", "DifficultyTier", "Uptime"]];
		let gameId = 0;

		for (let i = 0; i < data.Regions.length; i++) {
			const region = data.Regions[i];

			let gameText = "";
			if (gameId != region.GameId) {
				gameId = region.GameId;
				gameText = `0x${stringUtil.bigIntToHexString(gameId)}`;
			}

			tableData.push([
				gameText,
				`0x${stringUtil.bigIntToHexString(region.RegionId)}`,
				region.Name,
				region.DifficultyTier,
				region.Uptime,
			]);
		}

		htmlUtil.createAndAppendTable(regionReportContainer, tableData);
	}
}

const AccountOperationResult = Object.freeze({
	SUCCESS: 0,
	GENERIC_FAILURE: 1,
	DATABASE_ERROR: 2,
	EMAIL_INVALID: 3,
	EMAIL_ALREADY_USED: 4,
	EMAIL_NOT_FOUND: 5,
	PLAYER_NAME_INVALID: 6,
	PLAYER_NAME_ALREADY_USED: 7,
	PASSWORD_INVALID: 8,
	FLAG_ALREADY_SET: 9,
	FLAG_NOT_SET: 10,
});

const createAccountTab = {
	tabName: "create-account",
	pendingAccountData: null,

	initialize() {
		document.getElementById("create-account-submit-button").onclick = () => this.createAccount();

		const submitOnEnter = function (event) {
			if (event.key === "Enter") {
				event.preventDefault();
				document.getElementById("create-account-submit-button").click();
			}
		};

		document.getElementById("create-account-email").addEventListener("keydown", submitOnEnter);
		document.getElementById("create-account-player-name").addEventListener("keydown", submitOnEnter);
		document.getElementById("create-account-password").addEventListener("keydown", submitOnEnter);
		document.getElementById("create-account-confirm-password").addEventListener("keydown", submitOnEnter);
	},

	createAccount() {
		const email = document.getElementById("create-account-email");
		const playerName = document.getElementById("create-account-player-name");
		const password = document.getElementById("create-account-password");
		const confirmPassword = document.getElementById("create-account-confirm-password");

		if (this.pendingAccountData != null)
			return;

		email.setCustomValidity("");
		confirmPassword.setCustomValidity("");

		if (email.reportValidity() == false || playerName.reportValidity() == false || password.reportValidity() == false)
			return;

		if (this.validateEmail(email.value) == false) {
			email.setCustomValidity("Invalid email.");
			email.reportValidity();
			return;
		}

		if (password.value != confirmPassword.value) {
			confirmPassword.setCustomValidity("Your passwords do not match.");
			confirmPassword.reportValidity();
			return;
		}

		this.pendingAccountData = {
			Email: email.value.toLowerCase(),
			PlayerName: playerName.value,
			Password: password.value,
		};

		apiUtil.post("/AccountManagement/Create", this.pendingAccountData, (result) => this.onCreateAccountResponse(result));
	},

	onCreateAccountResponse(response) {
		const resultCode = response != null ? response.Result : AccountOperationResult.GENERIC_FAILURE;
		const resultString = this.getAccountOperationResultString(resultCode);

		window.alert(resultString);
		
		this.pendingAccountData = null;
	},

	validateEmail(email) {
		const atIndex = email.indexOf('@');
		if (atIndex == -1)
			return false;

		const dotIndex = email.lastIndexOf('.');
		if (dotIndex < atIndex)
			return false;

		const topDomainLength = email.length - (dotIndex + 1);
		if (topDomainLength < 2)
			return false;

		return true;
	},

	getAccountOperationResultString(resultCode) {
		const email = this.pendingAccountData.Email;
		const playerName = this.pendingAccountData.PlayerName;

		switch (resultCode) {
			case AccountOperationResult.SUCCESS:
				return `Created account ${email} (${playerName}).`;
			case AccountOperationResult.GENERIC_FAILURE:
				return `Generic failure.`;
			case AccountOperationResult.DATABASE_ERROR:
				return `Database error.`;
			case AccountOperationResult.EMAIL_INVALID:
				return `'${email}' is not a valid email address.`;
			case AccountOperationResult.EMAIL_ALREADY_USED:
				return `Email ${email} is already used by another account.`;
			case AccountOperationResult.EMAIL_NOT_FOUND:
				return `Account with email ${email} not found.`;
			case AccountOperationResult.PLAYER_NAME_INVALID:
				return `Names may contain only up to 16 alphanumeric characters.`;
			case AccountOperationResult.PLAYER_NAME_ALREADY_USED:
				return `Name ${playerName} is already used by another account.`;
			case AccountOperationResult.PASSWORD_INVALID:
				return `Password must between 3 and 64 characters long.`;
			case AccountOperationResult.FLAG_ALREADY_SET:
				return `Flag already set.`;
			case AccountOperationResult.FLAG_NOT_SET:
				return `Flag not set.`;
			default:
				return `Unknown error (code ${resultCode}).`;
		}
	}
}

tabManager.initialize([
	serverStatusTab,
	metricsTab,
	regionReportTab,
	createAccountTab
]);

})();

