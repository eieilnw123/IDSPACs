// wwwroot/js/dashboard.js
class HybridDashboard {
    constructor() {
        this.logConnection = null;
        this.autoScroll = true;
        this.currentLogLevel = 'all';
        this.worklistData = [];
        this.filteredData = [];
        this.currentPage = 1;
        this.itemsPerPage = 10;
        this.maxLogs = 200;
        this.pollInterval = 20000; // 20 seconds
        this.pollTimer = null;
    }

    async initialize() {
        console.log('🚀 Initializing Hybrid Dashboard...');

        try {
            // 1. Initialize real-time logs
            await this.initializeLogConnection();

            // 2. Start dashboard data polling
            this.startDashboardPolling();

            // 3. Load initial data
            await this.loadDashboardData();
            await this.loadWorklistData();

            // 4. Setup UI event listeners
            this.setupEventListeners();

            console.log('✅ Dashboard initialized successfully');
        } catch (error) {
            console.error('❌ Dashboard initialization failed:', error);
            this.showError('Failed to initialize dashboard');
        }
    }

    // ===== REAL-TIME LOGS (SignalR) =====
    async initializeLogConnection() {
        this.logConnection = new signalR.HubConnectionBuilder()
            .withUrl("/logHub")
            .withAutomaticReconnect([0, 2000, 10000, 30000])
            .build();

        // Connection events
        this.logConnection.onreconnecting((error) => {
            console.warn('🔄 Log connection reconnecting...', error);
            this.updateLogConnectionStatus('connecting');
        });

        this.logConnection.onreconnected((connectionId) => {
            console.log('✅ Log connection reconnected:', connectionId);
            this.updateLogConnectionStatus('connected');
        });

        this.logConnection.onclose((error) => {
            console.error('❌ Log connection closed:', error);
            this.updateLogConnectionStatus('disconnected');
        });

        // Handle incoming logs
        this.logConnection.on("NewLogEntry", (logEntry) => {
            this.addLogToUI(logEntry);
        });

        try {
            await this.logConnection.start();
            await this.logConnection.invoke("JoinLogGroup");
            console.log('🔗 Log SignalR connected');
            this.updateLogConnectionStatus('connected');
        } catch (error) {
            console.error('❌ Log SignalR connection failed:', error);
            this.updateLogConnectionStatus('disconnected');
        }
    }

    updateLogConnectionStatus(status) {
        const statusElement = document.getElementById('connectionStatus');
        if (statusElement) {
            switch (status) {
                case 'connected':
                    statusElement.textContent = '🟢 Connected (Logs: Real-time)';
                    statusElement.className = 'connection-status connected';
                    break;
                case 'connecting':
                    statusElement.textContent = '🟡 Reconnecting...';
                    statusElement.className = 'connection-status log-connecting';
                    break;
                default:
                    statusElement.textContent = '🔴 Disconnected (Logs: Offline)';
                    statusElement.className = 'connection-status disconnected';
            }
        }
    }

    addLogToUI(logEntry) {
        const logsContainer = document.getElementById('logsContainer');
        if (!logsContainer) return;

        const logElement = document.createElement('div');
        logElement.className = `log-entry log-${logEntry.level.toLowerCase()}`;
        logElement.innerHTML = `[${logEntry.timestamp}] [${logEntry.source}] ${this.escapeHtml(logEntry.message)}`;

        logsContainer.appendChild(logElement);

        // Remove old logs
        while (logsContainer.children.length > this.maxLogs) {
            logsContainer.removeChild(logsContainer.firstChild);
        }

        // Auto scroll
        if (this.autoScroll) {
            logsContainer.scrollTop = logsContainer.scrollHeight;
        }

        // Apply current filter
        this.filterLogs();
    }

    // ===== DASHBOARD DATA (REST API Polling) =====
    startDashboardPolling() {
        // Initial load
        this.loadDashboardData();

        // Set up polling
        this.pollTimer = setInterval(() => {
            this.loadDashboardData();
        }, this.pollInterval);

        console.log(`📊 Dashboard polling started (${this.pollInterval / 1000}s interval)`);
    }

    async loadDashboardData() {
        try {
            const response = await fetch('/api/dashboard/status');
            if (!response.ok) throw new Error(`HTTP ${response.status}`);

            const data = await response.json();
            this.updateDashboard(data);
            this.updateLastRefresh();
        } catch (error) {
            console.error('❌ Dashboard data loading failed:', error);
            this.showError('Failed to load dashboard data');
        }
    }

    async loadWorklistData() {
        try {
            const statusFilter = document.getElementById('statusFilter')?.value || 'all';
            const searchText = document.getElementById('patientSearch')?.value || '';

            const params = new URLSearchParams({
                page: this.currentPage,
                pageSize: this.itemsPerPage,
                ...(statusFilter !== 'all' && { status: statusFilter }),
                ...(searchText && { search: searchText })
            });

            const response = await fetch(`/api/dashboard/worklist?${params}`);
            if (!response.ok) throw new Error(`HTTP ${response.status}`);

            const data = await response.json();
            this.updateWorklistTable(data);
        } catch (error) {
            console.error('❌ Worklist data loading failed:', error);
        }
    }

    updateDashboard(data) {
        try {
            // Update all dashboard elements
            this.updateElement('totalItems', data.totalItems);
            this.updateElement('completedCount', data.completedItems);
            this.updateElement('pendingCount', data.pendingItems);
            this.updateElement('failedCount', data.failedItems);
            this.updateElement('todayCount', data.todayItems);
            this.updateElement('lastSync', data.lastSyncTime);
            this.updateElement('completionRate', Math.round(data.completionRate) + '%');

            // PDF monitoring
            this.updateElement('pendingPdfs', data.pdfPendingCount);
            this.updateElement('pdfStatusText', data.pdfStatusText);

            // PDF processing
            this.updateElement('processingQueue', data.procQueueSize);
            this.updateElement('activeProcessing', data.procActiveCount);
            this.updateElement('maxProcessing', data.procMaxConcurrent);

            // DICOM creation
            this.updateElement('dicomQueue', data.dicomQueueSize);
            this.updateElement('dicomCount', data.dicomFileCount);

            // Performance
            this.updateElement('successRate', Math.round(data.successRate) + '%');

            // Progress bars
            this.updateProgressBar('syncProgress', data.completionRate);
            const procProgress = data.procMaxConcurrent > 0 ? (data.procActiveCount / data.procMaxConcurrent) * 100 : 0;
            this.updateProgressBar('processingProgress', procProgress);

            // Status indicators
            const pdfStatus = document.getElementById('pdfStatus');
            if (pdfStatus) {
                pdfStatus.className = `status-indicator ${data.pdfIsMonitoring ? 'status-online' : 'status-offline'}`;
            }

            // System health
            this.updateSystemHealth(data.systemHealth);

        } catch (error) {
            console.error('❌ Error updating dashboard:', error);
        }
    }

    updateElement(id, value) {
        const element = document.getElementById(id);
        if (element) {
            element.textContent = value;
        }
    }

    updateProgressBar(id, percentage) {
        const element = document.getElementById(id);
        if (element) {
            element.style.width = Math.min(Math.max(percentage, 0), 100) + '%';
        }
    }

    updateSystemHealth(health) {
        const element = document.getElementById('systemStatus');
        if (element) {
            switch (health) {
                case 'healthy':
                    element.innerHTML = '🟢 Healthy';
                    element.style.color = '#4CAF50';
                    break;
                case 'warning':
                    element.innerHTML = '🟡 Warning';
                    element.style.color = '#ff9800';
                    break;
                case 'critical':
                    element.innerHTML = '🔴 Critical';
                    element.style.color = '#f44336';
                    break;
                default:
                    element.innerHTML = '❓ Unknown';
                    element.style.color = '#666';
            }
        }
    }

    updateLastRefresh() {
        const element = document.getElementById('lastUpdate');
        if (element) {
            element.textContent = new Date().toLocaleTimeString();
        }
    }

    // ===== WORKLIST TABLE =====
    updateWorklistTable(data) {
        this.worklistData = data.items;
        this.renderTable();
        this.updatePagination(data);
    }

    renderTable() {
        const tableBody = document.getElementById('worklistTableBody');
        if (!tableBody) return;

        tableBody.innerHTML = '';

        this.worklistData.forEach(item => {
            const row = this.createTableRow(item);
            tableBody.appendChild(row);
        });
    }

    createTableRow(item) {
        const row = document.createElement('tr');

        // Patient Info
        const patientCell = document.createElement('td');
        patientCell.innerHTML = `
            <div class="patient-info">${item.patientId}</div>
            <div class="patient-details">${item.patientName} (${item.patientSex}, ${item.patientAge || '?'}ปี)</div>
        `;
        row.appendChild(patientCell);

        // Accession
        const accessionCell = document.createElement('td');
        accessionCell.textContent = item.accessionNumber;
        row.appendChild(accessionCell);

        // Status
        const statusCell = document.createElement('td');
        const statusClass = `status-${item.status.toLowerCase().replace('_', '-')}`;
        statusCell.innerHTML = `<span class="status-badge ${statusClass}">${this.getStatusDisplayName(item.status)}</span>`;
        row.appendChild(statusCell);

        // Progress
        const progressCell = document.createElement('td');
        progressCell.className = 'progress-cell';
        const progressClass = this.getProgressClass(item.progress);
        progressCell.innerHTML = `
            <div class="mini-progress">
                <div class="mini-progress-fill ${progressClass}" style="width: ${item.progress}%"></div>
            </div>
            <div style="text-align: center; font-size: 0.75rem; margin-top: 2px;">${item.progress}%</div>
        `;
        row.appendChild(progressCell);

        // Files
        const filesCell = document.createElement('td');
        filesCell.innerHTML = `
            <div class="file-status">
                <span class="file-icon ${item.hasPdf ? 'file-available' : 'file-missing'}" title="PDF">📄</span>
                <span class="file-icon ${item.hasJpeg ? 'file-available' : 'file-missing'}" title="JPEG">🖼️</span>
                <span class="file-icon ${item.hasDicom ? 'file-available' : 'file-missing'}" title="DICOM">🏥</span>
            </div>
        `;
        row.appendChild(filesCell);

        // Scheduled
        const scheduledCell = document.createElement('td');
        scheduledCell.textContent = item.scheduledTime;
        row.appendChild(scheduledCell);

        // Updated
        const updatedCell = document.createElement('td');
        updatedCell.textContent = item.updatedTime;
        row.appendChild(updatedCell);

        return row;
    }

    getStatusDisplayName(status) {
        const statusMap = {
            'SCHEDULED': 'รอไฟล์',
            'PDF_RECEIVED': 'ได้รับ PDF',
            'JPEG_GENERATED': 'แปลง JPEG',
            'DICOM_CREATED': 'สร้าง DICOM',
            'COMPLETED': 'เสร็จสิ้น',
            'FAILED': 'ผิดพลาด'
        };
        return statusMap[status] || status;
    }

    getProgressClass(progress) {
        if (progress === 0) return 'progress-0';
        if (progress <= 25) return 'progress-25';
        if (progress <= 50) return 'progress-50';
        if (progress <= 75) return 'progress-75';
        return 'progress-100';
    }

    updatePagination(data) {
        const paginationInfo = document.getElementById('paginationInfo');
        if (paginationInfo) {
            const start = (data.page - 1) * data.pageSize + 1;
            const end = Math.min(data.page * data.pageSize, data.totalCount);
            paginationInfo.textContent = `Showing ${start} - ${end} of ${data.totalCount} items`;
        }

        // Update pagination controls
        const paginationControls = document.getElementById('paginationControls');
        if (!paginationControls) return;

        paginationControls.innerHTML = '';

        // Previous button
        const prevBtn = document.createElement('button');
        prevBtn.className = 'pagination-btn';
        prevBtn.textContent = '‹ Previous';
        prevBtn.disabled = data.page === 1;
        prevBtn.onclick = () => this.goToPage(data.page - 1);
        paginationControls.appendChild(prevBtn);

        // Page numbers
        const maxVisible = 5;
        let startPage = Math.max(1, data.page - Math.floor(maxVisible / 2));
        let endPage = Math.min(data.totalPages, startPage + maxVisible - 1);

        if (endPage - startPage + 1 < maxVisible) {
            startPage = Math.max(1, endPage - maxVisible + 1);
        }

        for (let i = startPage; i <= endPage; i++) {
            const pageBtn = document.createElement('button');
            pageBtn.className = `pagination-btn ${i === data.page ? 'active' : ''}`;
            pageBtn.textContent = i;
            pageBtn.onclick = () => this.goToPage(i);
            paginationControls.appendChild(pageBtn);
        }

        // Next button
        const nextBtn = document.createElement('button');
        nextBtn.className = 'pagination-btn';
        nextBtn.textContent = 'Next ›';
        nextBtn.disabled = data.page === data.totalPages;
        nextBtn.onclick = () => this.goToPage(data.page + 1);
        paginationControls.appendChild(nextBtn);
    }

    goToPage(page) {
        this.currentPage = page;
        this.loadWorklistData();
    }

    // ===== MANUAL ACTIONS =====
    async triggerManualSync() {
        await this.executeAction('/api/dashboard/actions/sync', 'Manual Sync', 'sync');
    }

    async retryFailedItems() {
        await this.executeAction('/api/dashboard/actions/retry-failed', 'Retry Failed', 'retry');
    }

    async processPendingFiles() {
        await this.executeAction('/api/dashboard/actions/process-pending', 'Process Pending', 'process');
    }

    async executeAction(url, actionName, buttonType) {
        const button = event?.target;
        if (button) {
            button.disabled = true;
            button.textContent = `🔄 ${buttonType === 'sync' ? 'Syncing...' : buttonType === 'retry' ? 'Retrying...' : 'Processing...'}`;
        }

        try {
            const response = await fetch(url, { method: 'POST' });
            const result = await response.json();

            if (result.success) {
                console.log(`✅ ${actionName} completed:`, result.message);
                // Refresh dashboard data after successful action
                setTimeout(() => {
                    this.loadDashboardData();
                    this.loadWorklistData();
                }, 2000);
            } else {
                console.error(`❌ ${actionName} failed:`, result.message);
                this.showError(`${actionName} failed: ${result.message}`);
            }
        } catch (error) {
            console.error(`❌ ${actionName} error:`, error);
            this.showError(`${actionName} error: ${error.message}`);
        } finally {
            if (button) {
                button.disabled = false;
                button.textContent = `🔄 ${actionName}`;
            }
        }
    }

    // ===== TAB MANAGEMENT =====
    showTab(tabName) {
        // Update tab buttons
        document.querySelectorAll('.tab-btn').forEach(btn => {
            btn.classList.remove('active');
        });
        event?.target?.classList.add('active');

        // Show/hide content
        const overviewTab = document.getElementById('overview-tab');
        const logsTab = document.getElementById('logs-tab');

        if (tabName === 'overview') {
            if (overviewTab) overviewTab.style.display = 'block';
            if (logsTab) logsTab.style.display = 'none';
        } else if (tabName === 'logs') {
            if (overviewTab) overviewTab.style.display = 'none';
            if (logsTab) logsTab.style.display = 'block';
        }
    }

    // ===== LOG MANAGEMENT =====
    toggleLogLevel(level) {
        this.currentLogLevel = level;

        // Update button states
        document.querySelectorAll('.log-btn').forEach(btn => {
            btn.classList.remove('active');
        });
        event?.target?.classList.add('active');

        this.filterLogs();
    }

    filterLogs() {
        const logs = document.querySelectorAll('.log-entry');
        logs.forEach(log => {
            if (this.currentLogLevel === 'all') {
                log.style.display = 'block';
            } else {
                const isVisible = log.classList.contains(`log-${this.currentLogLevel.toLowerCase()}`);
                log.style.display = isVisible ? 'block' : 'none';
            }
        });
    }

    clearLogs() {
        const logsContainer = document.getElementById('logsContainer');
        if (logsContainer) {
            logsContainer.innerHTML = '';
        }
    }

    toggleAutoScroll() {
        this.autoScroll = !this.autoScroll;
        const btn = event?.target;
        if (btn) {
            btn.textContent = this.autoScroll ? 'Auto Scroll' : 'Manual';
            btn.classList.toggle('active', this.autoScroll);
        }
    }

    // ===== FILE MANAGEMENT =====
    async checkFileLocation() {
        const fileName = document.getElementById('checkFileName')?.value?.trim();
        if (!fileName) {
            alert('Please enter a filename');
            return;
        }

        try {
            const response = await fetch(`/api/files/check/${encodeURIComponent(fileName)}`);
            const data = await response.json();

            const resultDiv = document.getElementById('file-check-result');
            if (!resultDiv) return;

            if (data.found) {
                resultDiv.innerHTML = `
                   <div style="background: #e8f5e8; border: 1px solid #4CAF50; border-radius: 5px; padding: 15px;">
                       <h6 style="color: #2e7d32; margin-bottom: 10px;">✅ File found in ${data.locations.length} location(s):</h6>
                       ${data.locations.map(loc => `
                           <div style="margin-bottom: 10px; padding: 10px; background: white; border-radius: 3px;">
                               <strong>📁 ${loc.folder}</strong><br>
                               <small>Size: ${loc.sizeFormatted} | Created: ${new Date(loc.created).toLocaleString()}</small>
                               ${loc.folder === 'UnmatchedPDFs' ? `
                                   <br><button onclick="dashboard.reprocessFile('${fileName}')" 
                                   style="margin-top: 5px; padding: 5px 10px; background: #ff9800; color: white; border: none; border-radius: 3px; cursor: pointer;">
                                   🔄 Reprocess File</button>
                               ` : ''}
                           </div>
                       `).join('')}
                   </div>
               `;
            } else {
                resultDiv.innerHTML = `
                   <div style="background: #fff3cd; border: 1px solid #ffc107; border-radius: 5px; padding: 15px;">
                       <h6 style="color: #856404;">❌ File <strong>${fileName}</strong> not found in any folder</h6>
                       <small>Check if the filename is correct or if the file has been processed.</small>
                   </div>
               `;
            }
        } catch (error) {
            const resultDiv = document.getElementById('file-check-result');
            if (resultDiv) {
                resultDiv.innerHTML = `
                   <div style="background: #f8d7da; border: 1px solid #f5c6cb; border-radius: 5px; padding: 15px; color: #721c24;">
                       ❌ Error checking file: ${error.message}
                   </div>
               `;
            }
        }
    }

    async reprocessFile(fileName) {
        try {
            const response = await fetch(`/api/files/reprocess/${encodeURIComponent(fileName)}`, {
                method: 'POST'
            });
            const data = await response.json();

            if (data.success) {
                alert(`✅ ${data.message}`);
                // Refresh file check
                await this.checkFileLocation();
            } else {
                alert(`❌ ${data.message}`);
            }
        } catch (error) {
            alert(`❌ Error: ${error.message}`);
        }
    }

    async loadRecentLogs() {
        try {
            const response = await fetch('/api/logs/recent?lines=100');
            const data = await response.json();

            const resultDiv = document.getElementById('log-search-result');
            if (!resultDiv) return;

            if (data.success) {
                resultDiv.innerHTML = `
                   <div style="margin-bottom: 10px; color: #666; border-bottom: 1px solid #ddd; padding-bottom: 5px;">
                       <strong>Recent Logs</strong> | File: ${data.file} | Total Lines: ${data.totalLines}
                   </div>
                   ${data.logs.map(line => {
                    let color = '#333';
                    if (line.includes('[ERR]')) color = '#d32f2f';
                    else if (line.includes('[WRN]')) color = '#f57c00';
                    else if (line.includes('[INF]')) color = '#1976d2';
                    else if (line.includes('[DBG]')) color = '#666';

                    return `<div style="color: ${color}; margin-bottom: 1px; line-height: 1.3;">${this.escapeHtml(line)}</div>`;
                }).join('')}
               `;
            } else {
                resultDiv.innerHTML = `<div style="color: #d32f2f;">❌ Error: ${data.message}</div>`;
            }
        } catch (error) {
            const resultDiv = document.getElementById('log-search-result');
            if (resultDiv) {
                resultDiv.innerHTML = `<div style="color: #d32f2f;">❌ Error loading logs: ${error.message}</div>`;
            }
        }
    }

    async searchLogs() {
        const keyword = document.getElementById('logSearchKeyword')?.value?.trim();
        if (!keyword) {
            alert('Please enter a search keyword');
            return;
        }

        try {
            const hours = document.getElementById('logSearchHours')?.value || '24';
            const response = await fetch(`/api/logs/search/${encodeURIComponent(keyword)}?hours=${hours}`);
            const data = await response.json();

            const resultDiv = document.getElementById('log-search-result');
            if (!resultDiv) return;

            if (data.success) {
                resultDiv.innerHTML = `
                   <div style="margin-bottom: 10px; color: #666; border-bottom: 1px solid #ddd; padding-bottom: 5px;">
                       <strong>Search Results for "${keyword}"</strong> | ${data.timeRange} | Matches: ${data.matchCount}
                   </div>
                   ${data.matches.map(match => `
                       <div style="margin-bottom: 8px; padding: 5px; background: rgba(255,255,255,0.5); border-radius: 3px;">
                           <small style="color: #666;">[${match.file}:${match.line}]</small><br>
                           <span style="font-family: monospace;">${this.escapeHtml(match.content)}</span>
                       </div>
                   `).join('')}
               `;
            } else {
                resultDiv.innerHTML = `<div style="color: #d32f2f;">❌ Error: ${data.message}</div>`;
            }
        } catch (error) {
            const resultDiv = document.getElementById('log-search-result');
            if (resultDiv) {
                resultDiv.innerHTML = `<div style="color: #d32f2f;">❌ Error searching logs: ${error.message}</div>`;
            }
        }
    }

    async loadFolderInfo() {
        try {
            const response = await fetch('/api/files/folders');
            const data = await response.json();

            const resultDiv = document.getElementById('folder-browser');
            if (!resultDiv) return;

            if (data.success) {
                resultDiv.innerHTML = data.folders.map(folder => `
                   <div style="margin-bottom: 20px; padding: 15px; border: 1px solid #ddd; border-radius: 5px; background: rgba(255,255,255,0.8);">
                       <h6 style="margin-bottom: 10px; display: flex; align-items: center; gap: 10px;">
                           📁 <strong>${folder.folder}</strong> 
                           ${folder.exists ?
                        `<span style="color: #4CAF50; font-size: 0.9em;">(${folder.fileCount} files)</span>` :
                        `<span style="color: #f44336; font-size: 0.9em;">(Folder not found)</span>`
                    }
                       </h6>
                       ${folder.exists ? `
                           <div style="font-size: 12px; color: #666; margin-bottom: 10px; font-family: monospace;">
                               📍 ${folder.path}
                           </div>
                           ${folder.files.length > 0 ? `
                               <div style="background: #f8f9fa; padding: 10px; border-radius: 3px; max-height: 200px; overflow-y: auto;">
                                   <strong style="color: #333;">Recent files (showing ${folder.files.length}/${folder.fileCount}):</strong>
                                   <div style="margin-top: 5px;">
                                       ${folder.files.map(file => `
                                           <div style="margin: 3px 0; font-family: monospace; font-size: 11px; padding: 2px 5px; background: white; border-radius: 2px;">
                                               📄 <strong>${file.name}</strong> 
                                               <span style="color: #666;">(${file.sizeFormatted}) - ${new Date(file.created).toLocaleString()}</span>
                                           </div>
                                       `).join('')}
                                   </div>
                               </div>
                           ` : '<div style="color: #666; font-style: italic; padding: 10px; background: #f8f9fa; border-radius: 3px;">No PDF files found</div>'}
                       ` : '<div style="color: #f44336; font-style: italic;">❌ Folder does not exist</div>'}
                   </div>
               `).join('');
            } else {
                resultDiv.innerHTML = `<div style="color: #d32f2f;">❌ Error loading folder information</div>`;
            }
        } catch (error) {
            const resultDiv = document.getElementById('folder-browser');
            if (resultDiv) {
                resultDiv.innerHTML = `<div style="color: #d32f2f;">❌ Error: ${error.message}</div>`;
            }
        }
    }

    // ===== EVENT LISTENERS =====
    setupEventListeners() {
        // Table filtering
        const statusFilter = document.getElementById('statusFilter');
        const patientSearch = document.getElementById('patientSearch');

        statusFilter?.addEventListener('change', () => {
            this.currentPage = 1;
            this.loadWorklistData();
        });

        patientSearch?.addEventListener('keyup', () => {
            this.currentPage = 1;
            this.loadWorklistData();
        });

        // Enter key for search inputs
        document.getElementById('logSearchKeyword')?.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') this.searchLogs();
        });

        document.getElementById('checkFileName')?.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') this.checkFileLocation();
        });
    }

    // ===== UTILITY METHODS =====
    refreshDashboard() {
        console.log('🔄 Manual refresh requested');
        this.loadDashboardData();
        this.loadWorklistData();
    }

    showError(message) {
        console.error('❌', message);
        // You could implement a toast notification system here
    }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // ===== CLEANUP =====
    destroy() {
        if (this.pollTimer) {
            clearInterval(this.pollTimer);
        }

        if (this.logConnection) {
            this.logConnection.stop();
        }
    }
}

// ===== GLOBAL INSTANCE =====
let dashboard;

// Initialize when page loads
document.addEventListener('DOMContentLoaded', async () => {
    dashboard = new HybridDashboard();
    await dashboard.initialize();
});

// Global functions for HTML onclick events
function showTab(tabName) {
    dashboard?.showTab(tabName);
}

function toggleLogLevel(level) {
    dashboard?.toggleLogLevel(level);
}

function clearLogs() {
    dashboard?.clearLogs();
}

function toggleAutoScroll() {
    dashboard?.toggleAutoScroll();
}

function triggerManualSync() {
    dashboard?.triggerManualSync();
}

function retryFailedItems() {
    dashboard?.retryFailedItems();
}

function processPendingFiles() {
    dashboard?.processPendingFiles();
}

function refreshDashboard() {
    dashboard?.refreshDashboard();
}

function filterTable() {
    dashboard?.loadWorklistData();
}

function checkFileLocation() {
    dashboard?.checkFileLocation();
}

function searchLogs() {
    dashboard?.searchLogs();
}

function loadRecentLogs() {
    dashboard?.loadRecentLogs();
}

function loadFolderInfo() {
    dashboard?.loadFolderInfo();
}

// Cleanup on page unload
window.addEventListener('beforeunload', () => {
    dashboard?.destroy();
});