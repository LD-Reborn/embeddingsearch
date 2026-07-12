class Dashboard {
    constructor() {
        this.workersContainer = document.getElementById('workersContainer');
        this.loadingWorkers = new Set();
        this.lastWorkerData = null;

        // SSE connections per worker
        this.eventSources = new Map();

        // Log buffers per worker (max 50, newest first)
        this.logBuffers = new Map();

        // Server-side log level counts per worker
        this.logCounts = new Map();

        // Modal state
        this.modalWorkerName = null;
        this.modalLogs = [];

        this.init();
    }

    init() {
        this.loadWorkers();
        this.fetchLogCounts();

        this.refreshInterval = setInterval(() => {
            this.loadWorkers();
            this.fetchLogCounts();
        }, 5000);

        // Event delegation for buttons
        document.addEventListener('click', (e) => {
            if (e.target.classList.contains('load-all-logs')) {
                e.preventDefault();
                this.openLogModal(e.target.dataset.worker);
            }
        });

        // Modal lifecycle
        document.getElementById('logHistoryModal').addEventListener('hidden.bs.modal', () => this.closeModal());
    }

    // ==================== Log Counts ====================

    async fetchLogCounts() {
        try {
            const response = await fetch('/dashboard/logcounts');
            if (!response.ok) return;
            const data = await response.json();
            if (data.Success) {
                for (const [worker, counts] of Object.entries(data.Counts)) {
                    this.logCounts.set(worker, counts);
                }
                this.refreshAllLogCounters();
            }
        } catch (error) {
            console.error('Error fetching log counts:', error);
        }
    }

    refreshAllLogCounters() {
        for (const [workerName, counts] of this.logCounts) {
            const card = this.workersContainer.querySelector(`[data-worker-name="${this.escapeHtml(workerName)}"]`);
            if (!card) continue;
            const levelEl = card.querySelector('[data-field="log-levels"]');
            if (levelEl) {
                levelEl.innerHTML = `<small>${this.renderLogLevelCounts(counts)}</small>`;
            }
        }
    }

    incrementLogLevel(workerName, logLevel) {
        let counts = this.logCounts.get(workerName);
        if (!counts) {
            counts = {};
            this.logCounts.set(workerName, counts);
        }
        const name = this.resolveLogLevel(logLevel);
        counts[name] = (counts[name] || 0) + 1;
    }

    // ==================== SSE ====================

    openEventSource(workerName) {
        if (this.eventSources.has(workerName)) return;

        const es = new EventSource(`/dashboard/logs/stream?workerName=${encodeURIComponent(workerName)}`);

        es.addEventListener('log', (e) => {
            const log = JSON.parse(e.data);
            this.onLogReceived(workerName, log);
        });

        this.eventSources.set(workerName, es);
    }

    closeEventSource(workerName) {
        const es = this.eventSources.get(workerName);
        if (es) {
            es.close();
            this.eventSources.delete(workerName);
        }
    }

    onLogReceived(workerName, log) {
        let buffer = this.logBuffers.get(workerName);
        if (!buffer) {
            buffer = [];
            this.logBuffers.set(workerName, buffer);
        }
        buffer.unshift(log);
        if (buffer.length > 50) buffer.length = 50;

        this.incrementLogLevel(workerName, log.logLevel);
        this.updateCardLogs(workerName);

        if (this.modalWorkerName === workerName) {
            this.appendModalLog(log);
        }
    }

    // ==================== Card Log Rendering ====================

    updateCardLogs(workerName) {
        const card = this.workersContainer.querySelector(`[data-worker-name="${this.escapeHtml(workerName)}"]`);
        if (!card) return;

        const counts = this.logCounts.get(workerName) || {};

        const levelEl = card.querySelector('[data-field="log-levels"]');
        if (levelEl) {
            levelEl.innerHTML = `<small>${this.renderLogLevelCounts(counts)}</small>`;
        }
    }

    renderLogEntry(log) {
        const levelName = this.resolveLogLevel(log.logLevel);
        return `
            <li class="list-group-item p-1 bg-transparent border-secondary d-flex log-entry">
                <div class="${this.classFromLogLevel(log.logLevel)} log-level-bar" style="width:4px;" role="img" aria-label="${levelName}" title="${levelName}"></div>
                <div class="py-1 px-2 flex-grow-1">
                    <small class="text-muted">
                        ${this.formatDateTime(log.timestamp)}
                        ${log.workerCallId != null ? (" - " + log.workerCallId) : ""}
                    </small>
                    <br>
                    <span>${this.escapeHtml(this.formatLogMessage(log))}</span>
                </div>
            </li>
        `;
    }

    // ==================== Worker Loading & Rendering ====================

    async loadWorkers() {
        try {
            const response = await fetch('/dashboard/workers');
            if (!response.ok) throw new Error('Failed to load workers');

            const data = await response.json();
            this.renderWorkers(data);
            this.lastWorkerData = data;
        } catch (error) {
            console.error('Error loading workers:', error);
            this.workersContainer.innerHTML = `
                <div class="col-12">
                    <div class="alert alert-danger d-flex align-items-center justify-content-between">
                        <span>${window.dashboardTranslations.errorLoadingWorkers}</span>
                        <button class="btn btn-danger btn-sm" onclick="dashboard.loadWorkers()">${window.dashboardTranslations.retry}</button>
                    </div>
                </div>`;
        }
    }

    renderWorkers(data) {
        this.workersContainer.querySelectorAll(':scope > :not([data-worker-name])').forEach(el => el.remove());

        const existingCards = new Map();
        for (const card of this.workersContainer.querySelectorAll('[data-worker-name]')) {
            existingCards.set(card.dataset.workerName, card);
        }

        data.WorkerList.forEach(worker => {
            let card = existingCards.get(worker.Name);

            if (card) {
                existingCards.delete(worker.Name);

                const oldWorker = this._findWorkerData(worker.Name);
                if (this._workerDataChanged(oldWorker, worker)) {
                    card.innerHTML = this._cardHtml(worker);
                } else {
                    const execLabel = card.querySelector('[data-field="execution-label"]');
                    if (execLabel) {
                        execLabel.innerHTML = this._executionLabel(worker);
                    }
                }
            } else {
                card = document.createElement('div');
                card.className = 'col-lg-6';
                card.dataset.workerName = worker.Name;
                card.innerHTML = this._cardHtml(worker);
                this.workersContainer.appendChild(card);
            }
        });

        for (const [name, card] of existingCards) {
            card.remove();
            this.closeEventSource(name);
            this.logBuffers.delete(name);
        }

        // Ensure SSE is open for all active workers
        for (const worker of data.WorkerList) {
            this.openEventSource(worker.Name);
        }
    }

    _findWorkerData(name) {
        if (!this.lastWorkerData?.WorkerList) return null;
        return this.lastWorkerData.WorkerList.find(w => w.Name === name) || null;
    }

    _workerDataChanged(oldData, newData) {
        if (!oldData) return true;
        return JSON.stringify(oldData) !== JSON.stringify(newData);
    }

    _executionLabel(worker) {
        if (this.loadingWorkers.has(worker.Name)) {
            return '\u23F3';
        }
        return worker.IsExecuting
            ? '\uD83C\uDFC3\u200D\u2640\uFE0F ' + window.dashboardTranslations.running
            : '\uD83D\uDECF\uFE0F ' + window.dashboardTranslations.idle;
    }

    _cardHtml(worker) {
        const badgeClass = this.getBadgeClass(worker.HealthStatus);
        const statusText = this.getStatusText(worker.HealthStatus);

        const callsHtml = worker.Calls && worker.Calls.length > 0
            ? '<div data-field="calls" class="mb-1">' + this.createCallsList(worker.Calls, worker.Name) + '</div>'
            : '';

        const escapedName = this.escapeHtml(worker.Name);

        const counts = this.logCounts.get(worker.Name) || {};

        const logsHtml = `
            <hr>

            <div class="d-flex justify-content-between align-items-center mb-2">
                <h3 class="fs-5 mb-0">${window.dashboardTranslations.logs}</h3>
                <span data-field="log-levels" class="text-muted"><small>${this.renderLogLevelCounts(counts)}</small></span>
            </div>
            <button class="btn btn-primary btn-sm w-100 load-all-logs" data-worker="${escapedName}">
                ${window.dashboardTranslations.allLogs}
            </button>
        `;

        return `
            <div class="card shadow-sm h-100">
                <div class="card-header d-flex justify-content-between align-items-center">
                    <div class="text-truncate me-2">
                        <h2 class="fs-6 d-inline">${escapedName}</h2>
                        <small class="text-muted ms-2">${this.escapeHtml(worker.Script)}</small>
                    </div>
                    <span class="badge ${badgeClass} flex-shrink-0">${statusText}</span>
                </div>
                <div class="card-body">
                    <h3 class="visually-hidden">${window.dashboardTranslations.worker}</h3>
                    <div class="d-flex justify-content-between mb-1">
                        <span class="text-muted">${window.dashboardTranslations.execution}:</span>
                        <span data-field="execution-label">${this._executionLabel(worker)}</span>
                    </div>
                    <div class="d-flex justify-content-between mb-1">
                        <span class="text-muted">${window.dashboardTranslations.lastExecution}:</span>
                        <span>${worker.LastExecution ? this.formatDateTime(worker.LastExecution) : window.dashboardTranslations.notAvailable}</span>
                    </div>
                    <div class="d-flex justify-content-between mb-1">
                        <span class="text-muted">${window.dashboardTranslations.successful}:</span>
                        <span>${worker.LastSuccessfulExecution ? this.formatDateTime(worker.LastSuccessfulExecution) : window.dashboardTranslations.notAvailable}</span>
                    </div>
                    ${callsHtml}
                    ${logsHtml}
                </div>
                <div class="card-footer d-flex justify-content-end gap-2">
                    <button class="btn btn-primary me-auto" onclick="dashboard.triggerWorker('${worker.Name}')">
                        ${window.dashboardTranslations.trigger}
                    </button>
                    <div class="btn-group btn-group-sm">
                        <button class="btn btn-success" onclick="dashboard.toggleWorkerState('${worker.Name}', true)">
                            ${window.dashboardTranslations.enableAll}
                        </button>
                        <button class="btn btn-danger" onclick="dashboard.toggleWorkerState('${worker.Name}', false)">
                            ${window.dashboardTranslations.disableAll}
                        </button>
                    </div>
                </div>
            </div>
        `;
    }

    // ==================== Calls ====================

    createCallsList(calls, workerName) {
        return `
            <hr>
            <h3 class="fs-5 mb-2">${window.dashboardTranslations.calls} (${calls.length})</h3>
            <div class="list-group list-group-flush">
                ${calls.map(call => `
                    <div class="list-group-item d-flex justify-content-between align-items-center py-1 px-0 bg-transparent border-secondary">
                        <span class="text-truncate me-2">${this.escapeHtml(call.CallConfig.name)}</span>
                        <div class="d-flex align-items-center gap-1 flex-shrink-0">
                            <div class="d-flex border rounded overflow-hidden">
                                <span class="badge rounded-0 bg-dark">${call.IsExecuting ? '\uD83C\uDFC3\u200D\u2640\uFE0F' : '\uD83D\uDECF\uFE0F'} ${call.IsExecuting ? window.dashboardTranslations.running : window.dashboardTranslations.idle}</span>
                                <span class="badge rounded-0 ${this.getBadgeClass(call.HealthStatus)}">${call.HealthStatus}</span>
                                <span class="badge rounded-0 ${call.IsActive ? 'bg-success' : 'bg-secondary'}">${call.IsActive ? window.dashboardTranslations.enabled : window.dashboardTranslations.disabled}</span>
                            </div>
                            ${call.IsActive
                                ? `<button class="btn btn-sm btn-danger py-0 px-1" onclick="dashboard.toggleCallState('${workerName}', '${this.escapeHtml(call.CallConfig.name)}', false)">${window.dashboardTranslations.disable}</button>`
                                : `<button class="btn btn-sm btn-success py-0 px-1" onclick="dashboard.toggleCallState('${workerName}', '${this.escapeHtml(call.CallConfig.name)}', true)">${window.dashboardTranslations.enable}</button>`
                            }
                        </div>
                    </div>
                `).join('')}
            </div>
        `;
    }

    // ==================== Modal ====================

    isNearBottom(el, threshold = 100) {
        return el.scrollHeight - el.scrollTop - el.clientHeight < threshold;
    }

    scrollToBottom(el) {
        el.scrollTop = el.scrollHeight;
    }

    getModalScrollContainer() {
        return document.querySelector('#logHistoryModal .modal-body');
    }

    async openLogModal(workerName) {
        this.modalWorkerName = workerName;
        this.modalLogs = [];

        const escapedName = this.escapeHtml(workerName);
        document.getElementById('logHistoryModalTitle').textContent = `${window.dashboardTranslations.allLogs} \u2014 ${escapedName}`;
        document.getElementById('logHistoryList').innerHTML = `<li class="list-group-item text-center p-3"><div class="spinner-border spinner-border-sm"></div></li>`;
        document.getElementById('logHistoryCount').textContent = '';

        const modalEl = document.getElementById('logHistoryModal');
        const modal = new bootstrap.Modal(modalEl);
        modal.show();

        const onShown = () => {
            this.scrollToBottom(this.getModalScrollContainer());
            modalEl.removeEventListener('shown.bs.modal', onShown);
        };
        modalEl.addEventListener('shown.bs.modal', onShown);

        await this.fetchModalLogs();
    }

    async fetchModalLogs() {
        if (!this.modalWorkerName) return;

        try {
            const formData = new FormData();
            formData.append('workerName', this.modalWorkerName);
            formData.append('skip', '0');
            formData.append('count', '1000');

            const response = await fetch('/dashboard/logs', { method: 'POST', body: formData });
            if (!response.ok) throw new Error('Failed to load logs');

            const data = await response.json();

            if (data.Success && data.Logs.length > 0) {
                this.modalLogs = data.Logs;
                const list = document.getElementById('logHistoryList');
                list.innerHTML = data.Logs.map(log => this.renderLogEntry(log)).join('');
            } else {
                document.getElementById('logHistoryList').innerHTML = `<li class="list-group-item text-muted p-3 text-center">${window.dashboardTranslations.noMoreLogs}</li>`;
            }

            document.getElementById('logHistoryCount').textContent = `${this.modalLogs.length} logs`;
        } catch (error) {
            console.error('Error loading modal logs:', error);
            document.getElementById('logHistoryList').innerHTML = `<li class="list-group-item text-danger p-3 text-center">${window.dashboardTranslations.retry}</li>`;
        }
    }

    appendModalLog(log) {
        const list = document.getElementById('logHistoryList');
        if (!list) return;

        const modal = document.getElementById('logHistoryModal');
        if (!modal.classList.contains('show')) return;

        // Remove placeholder if present
        const placeholder = list.querySelector('.text-muted.text-center');
        if (placeholder) placeholder.remove();

        const container = this.getModalScrollContainer();
        const wasAtBottom = this.isNearBottom(container);

        list.insertAdjacentHTML('beforeend', this.renderLogEntry(log));

        const newEntry = list.lastElementChild;
        newEntry.classList.add('log-entry-highlight');
        newEntry.addEventListener('animationend', () => newEntry.classList.remove('log-entry-highlight'), { once: true });

        if (wasAtBottom) {
            this.scrollToBottom(container);
        }

        this.modalLogs.unshift(log);
        document.getElementById('logHistoryCount').textContent = `${this.modalLogs.length} logs`;
    }

    closeModal() {
        this.modalWorkerName = null;
        this.modalLogs = [];
    }

    // ==================== Worker Actions ====================

    async triggerWorker(name) {
        try {
            this.loadingWorkers.add(name);
            this.loadWorkers();

            const formData = new FormData();
            formData.append('name', name);

            const response = await fetch('/dashboard/trigger', {
                method: 'POST',
                body: formData
            });

            const result = await response.json();
            if (result.Success) {
                await new Promise(resolve => setTimeout(resolve, 500));
                await this.loadWorkers();
                this.loadingWorkers.delete(name);
                this.renderWorkers(this.lastWorkerData);
            }
        } catch (error) {
            console.error('Error triggering worker:', error);
            this.loadingWorkers.delete(name);
        }
    }

    async toggleWorkerState(name, enable) {
        try {
            const formData = new FormData();
            formData.append('workerName', name);
            formData.append('requestStop', 'false');

            const endpoint = enable ? '/dashboard/enable' : '/dashboard/disable';
            const response = await fetch(endpoint, {
                method: 'POST',
                body: formData
            });

            const result = await response.json();
            if (result.Success) {
                this.loadWorkers();
            }
        } catch (error) {
            console.error('Error toggling worker state:', error);
        }
    }

    async toggleCallState(workerName, callName, enable) {
        try {
            const formData = new FormData();
            formData.append('workerName', workerName);
            formData.append('callName', callName);

            const endpoint = enable ? '/dashboard/enable' : '/dashboard/disable';
            const response = await fetch(endpoint, {
                method: 'POST',
                body: formData
            });

            const result = await response.json();
            if (result.Success) {
                this.loadWorkers();
            }
        } catch (error) {
            console.error('Error toggling call state:', error);
        }
    }

    // ==================== Helpers ====================

    renderLogLevelCounts(counts) {
        const parts = [];
        const levels = ['trace', 'debug', 'information', 'info', 'warning', 'error', 'critical'];
        for (const level of levels) {
            if (counts[level] > 0) {
                const cls = this.classFromLogLevel(level);
                parts.push(`<span class="${cls} badge">${level}: ${counts[level]}</span>`);
            }
        }
        return parts.join(' ');
    }

    formatLogMessage(log) {
        let argIndex = 0;
        return log.message.replace(/\{[^}]+\}/g, () => {
            return log.args[argIndex++] ?? "";
        });
    }

    resolveLogLevel(level) {
        const map = { 0: 'trace', 1: 'debug', 2: 'information', 3: 'warning', 4: 'error', 5: 'critical' };
        const str = String(level).toLowerCase();
        if (map[str] !== undefined) return map[str];
        return str;
    }

    classFromLogLevel(level) {
        const name = this.resolveLogLevel(level);

        switch (name) {
            case 'trace':     return 'bg-muted';
            case 'debug':     return 'bg-secondary';
            case 'information':
            case 'info':      return 'bg-info';
            case 'warning':   return 'bg-warning';
            case 'error':     return 'bg-danger';
            case 'critical':  return 'bg-danger bg-gradient';
            default:          return '';
        }
    }

    getBadgeClass(status) {
        switch (status?.toLowerCase()) {
            case 'healthy': return 'bg-success';
            case 'degraded': return 'bg-warning';
            case 'unhealthy': return 'bg-danger';
            case 'executing': return 'bg-info';
            default: return 'bg-secondary';
        }
    }

    getStatusText(status) {
        switch (status?.toLowerCase()) {
            case 'healthy': return window.dashboardTranslations.healthy;
            case 'degraded': return window.dashboardTranslations.degraded;
            case 'unhealthy': return window.dashboardTranslations.unhealthy;
            case 'executing': return window.dashboardTranslations.executing;
            default: return '\u2753 ' + window.dashboardTranslations.unknown;
        }
    }

    formatDateTime(dateString) {
        if (!dateString) return window.dashboardTranslations.notAvailable;
        const date = new Date(dateString);
        return date.toLocaleString(window.dashboardCulture ?? navigator.language, {
            day: '2-digit',
            month: '2-digit',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit'
        });
    }

    escapeHtml(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

let dashboard;
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => { dashboard = new Dashboard(); });
} else {
    dashboard = new Dashboard();
}
