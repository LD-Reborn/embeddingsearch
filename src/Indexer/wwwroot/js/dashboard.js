class Dashboard {
    constructor() {
        this.workersContainer = document.getElementById('workersContainer');
        this.loadingWorkers = new Set();
        this.lastWorkerData = null;

        this.init();
    }

    init() {
        this.loadWorkers();

        this.refreshInterval = setInterval(() => this.loadWorkers(), 5000);
    }

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

        // Remove cards that no longer exist
        for (const card of existingCards.values()) {
            card.remove();
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
            ? '🏃‍➡️ ' + window.dashboardTranslations.running
            : '🛌 ' + window.dashboardTranslations.idle;
    }

    _cardHtml(worker) {
        const badgeClass = this.getBadgeClass(worker.HealthStatus);
        const statusText = this.getStatusText(worker.HealthStatus);

        const callsHtml = worker.Calls && worker.Calls.length > 0
            ? '<div data-field="calls">' + this.createCallsList(worker.Calls, worker.Name) + '</div>'
            : '';

        return `
            <div class="card shadow-sm h-100">
                <div class="card-header d-flex justify-content-between align-items-center">
                    <div class="text-truncate me-2">
                        <h2 class="fs-6 d-inline">${this.escapeHtml(worker.Name)}</h2>
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
                                <span class="badge rounded-0 ${call.IsExecuting ? 'bg-dark' : 'bg-dark'}">${call.IsExecuting ? '🏃‍➡️' : '🛌'} ${call.IsExecuting ? window.dashboardTranslations.running : window.dashboardTranslations.idle}</span>
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
document.addEventListener('DOMContentLoaded', () => {
    dashboard = new Dashboard();
});
