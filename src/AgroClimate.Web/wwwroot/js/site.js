(function () {
    "use strict";

    // 1. Переключение Светлой / Тёмной темы (единый ключ и поведение с ITS AgroVision V2)
    window.toggleMcTheme = function () {
        const html = document.documentElement;
        const current = html.getAttribute("data-theme") || "light";
        const next = current === "dark" ? "light" : "dark";
        html.setAttribute("data-theme", next);
        try { localStorage.setItem("agroclimate-theme", next); } catch (e) { }
        updateThemeToggleLabel(next);
        window.dispatchEvent(new CustomEvent("mc-theme-changed", { detail: { theme: next } }));
    };

    function updateThemeToggleLabel(theme) {
        const btn = document.getElementById("themeToggleBtn");
        if (btn) {
            btn.textContent = theme === "dark" ? "☀ Светлая" : "☾ Тёмная";
        }
    }

    document.addEventListener("DOMContentLoaded", function () {
        const current = document.documentElement.getAttribute("data-theme") || "light";
        updateThemeToggleLabel(current);
        initTableFilters();
        initCommandPalette();
        initSignalR();
    });

    // Re-bind instant table filters after HTMX swaps
    document.addEventListener("htmx:afterSwap", function () {
        initTableFilters();
    });

    // 2. Мгновенный клиентский фильтр строк таблиц (input[data-table-filter])
    function initTableFilters() {
        document.querySelectorAll("input[data-table-filter]").forEach(function (input) {
            if (input.dataset.boundFilter) return;
            input.dataset.boundFilter = "1";
            input.addEventListener("input", function () {
                const targetId = input.getAttribute("data-table-filter");
                const table = document.getElementById(targetId);
                if (!table) return;
                const q = input.value.trim().toLowerCase();
                table.querySelectorAll("tbody tr").forEach(function (row) {
                    row.style.display = !q || row.textContent.toLowerCase().includes(q) ? "" : "none";
                });
            });
        });
    }

    // 3. Командная палитра быстрого перехода Ctrl + K / Cmd + K (как в ITS AgroVision V2)
    function initCommandPalette() {
        const backdrop = document.getElementById("cmdkBackdrop");
        const input = document.getElementById("cmdkInput");
        const list = document.getElementById("cmdkList");
        const openBtn = document.getElementById("cmdkOpenBtn");
        if (!backdrop || !input || !list) return;

        let items = null;
        let filtered = [];
        let activeIdx = 0;

        async function loadIndex() {
            try {
                const res = await fetch("/UiLookup/SearchIndex");
                items = res.ok ? await res.json() : [];
            } catch {
                items = [];
            }
        }

        function render() {
            const q = input.value.trim().toLowerCase();
            filtered = (items || []).filter(x =>
                !q ||
                x.title.toLowerCase().includes(q) ||
                x.subtitle.toLowerCase().includes(q) ||
                x.category.toLowerCase().includes(q)).slice(0, 14);
            activeIdx = 0;

            if (filtered.length === 0) {
                list.innerHTML = '<li class="p-3 text-center text-muted small">Ничего не найдено</li>';
                return;
            }

            list.innerHTML = "";
            filtered.forEach((it, idx) => {
                const li = document.createElement("li");
                const a = document.createElement("a");
                a.href = it.url;
                a.className = "cmdk-item" + (idx === 0 ? " active" : "");

                const left = document.createElement("div");
                const st = document.createElement("strong");
                st.textContent = it.title;
                const sm = document.createElement("small");
                sm.textContent = it.subtitle;
                left.appendChild(st);
                left.appendChild(sm);

                const tag = document.createElement("span");
                tag.className = "cmdk-tag";
                tag.textContent = it.category;

                a.appendChild(left);
                a.appendChild(tag);
                li.appendChild(a);
                list.appendChild(li);
            });
        }

        async function openPalette() {
            backdrop.classList.add("open");
            input.value = "";
            await loadIndex();
            render();
            setTimeout(() => input.focus(), 20);
        }

        if (openBtn) openBtn.addEventListener("click", openPalette);
        backdrop.addEventListener("click", (e) => { if (e.target === backdrop) backdrop.classList.remove("open"); });
        input.addEventListener("input", render);

        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && backdrop.classList.contains("open")) {
                backdrop.classList.remove("open");
                return;
            }
            if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
                e.preventDefault();
                if (backdrop.classList.contains("open")) backdrop.classList.remove("open");
                else openPalette();
                return;
            }
            if (!backdrop.classList.contains("open") || filtered.length === 0) return;

            if (e.key === "ArrowDown") {
                e.preventDefault();
                activeIdx = (activeIdx + 1) % filtered.length;
            } else if (e.key === "ArrowUp") {
                e.preventDefault();
                activeIdx = (activeIdx - 1 + filtered.length) % filtered.length;
            } else if (e.key === "Enter") {
                e.preventDefault();
                if (filtered[activeIdx]) window.location.href = filtered[activeIdx].url;
                return;
            } else {
                return;
            }

            list.querySelectorAll(".cmdk-item").forEach((el, idx) => el.classList.toggle("active", idx === activeIdx));
        });
    }

    // 4. Всплывающие уведомления о тревогах
    function showToast(title, body, warRoomUrl, isCritical) {
        const container = document.getElementById("mc-toast-container");
        if (!container) return;

        const el = document.createElement("div");
        el.className = "mc-toast";
        el.style.borderLeftColor = isCritical ? "#dc2626" : "#d97706";

        const header = document.createElement("div");
        header.style.fontWeight = "700";
        header.style.marginBottom = "0.25rem";
        header.textContent = title;

        const msg = document.createElement("div");
        msg.style.color = "var(--agro-muted)";
        msg.textContent = body;

        el.appendChild(header);
        el.appendChild(msg);

        if (warRoomUrl) {
            const linkWrap = document.createElement("div");
            linkWrap.style.marginTop = "0.5rem";
            const a = document.createElement("a");
            a.href = warRoomUrl;
            a.className = "mc-btn mc-btn-danger mc-btn-sm";
            a.textContent = "Открыть Incident War Room →";
            linkWrap.appendChild(a);
            el.appendChild(linkWrap);
        }

        container.appendChild(el);
        setTimeout(() => {
            el.style.opacity = "0";
            el.style.transition = "opacity 0.35s ease";
            setTimeout(() => el.remove(), 400);
        }, 8000);
    }

    // 5. Живой поток телеметрии и событий War Room по SignalR
    function initSignalR() {
        if (typeof signalR === "undefined") return;

        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/climate")
            .withAutomaticReconnect([0, 1500, 3000, 5000])
            .build();

        window.agroSignalR = connection;

        const connDot = document.getElementById("sr-conn-dot");
        const connText = document.getElementById("sr-conn-text");

        function setConnBadge(online) {
            if (connDot) connDot.className = online ? "status-dot optimal" : "status-dot warning";
            if (connText) connText.textContent = online ? "SignalR Live" : "Переподключение…";
        }

        connection.onreconnecting(() => setConnBadge(false));
        connection.onreconnected(() => setConnBadge(true));
        connection.onclose(() => setConnBadge(false));

        connection.on("ReceiveZoneTelemetry", function (t, pipeline) {
            updatePipelineHeader(pipeline);
            updateDashboardZoneCard(t);
            window.dispatchEvent(new CustomEvent("agro:telemetry", { detail: t }));
        });

        connection.on("ReceiveIncidentEvent", function (ev) {
            if (ev.eventType === "IncidentOpened" || ev.eventType === "SeverityEscalated") {
                showToast(
                    `${ev.incidentNumber}: ${ev.zoneName}`,
                    ev.message,
                    `/Incidents/WarRoom/${ev.incidentId}`,
                    ev.severity === "Critical" || ev.severity === "Emergency"
                );
                updateGlobalEmergencyBanner(ev);
            }

            window.dispatchEvent(new CustomEvent("agro:incident", { detail: ev }));
        });

        connection.on("ReceiveSimulatorStatus", function (status) {
            const amb = document.getElementById("irkutsk-ambient-val");
            if (amb && typeof status.irkutskAmbientTempC === "number") {
                amb.textContent = (status.irkutskAmbientTempC > 0 ? "+" : "") + status.irkutskAmbientTempC.toFixed(1) + " °C";
            }
            const scTitle = document.getElementById("sim-active-title");
            if (scTitle && status.scenarioTitle) {
                scTitle.textContent = status.scenarioTitle;
            }
        });

        connection.start()
            .then(() => {
                setConnBadge(true);
                const warRoomEl = document.getElementById("warroom-root");
                if (warRoomEl && warRoomEl.dataset.incidentId) {
                    connection.invoke("JoinIncidentWarRoom", parseInt(warRoomEl.dataset.incidentId, 10)).catch(() => { });
                }
            })
            .catch(() => setConnBadge(false));
    }

    function updatePipelineHeader(p) {
        if (!p) return;
        const rowsEl = document.getElementById("pipe-total-rows");
        if (rowsEl && typeof p.columnstoreTotalRows === "number" && p.columnstoreTotalRows > 0) {
            rowsEl.textContent = p.columnstoreTotalRows.toLocaleString("ru-RU");
        }
        const kpiRowsEl = document.getElementById("kpi-columnstore-rows");
        if (kpiRowsEl && typeof p.columnstoreTotalRows === "number" && p.columnstoreTotalRows > 0) {
            kpiRowsEl.textContent = p.columnstoreTotalRows.toLocaleString("ru-RU");
        }
        const batchEl = document.getElementById("pipe-flush-ms");
        if (batchEl && typeof p.lastFlushDurationMs === "number") {
            batchEl.textContent = p.lastFlushDurationMs.toFixed(1) + " мс";
        }
    }

    function setCellValue(card, selector, text) {
        const el = card.querySelector(selector);
        if (el && el.textContent !== text) {
            el.textContent = text;
            const cell = el.closest(".metric-cell");
            if (cell) {
                cell.classList.remove("flash-update");
                void cell.offsetWidth;
                cell.classList.add("flash-update");
            }
        }
    }

    function updateDashboardZoneCard(t) {
        const card = document.querySelector(`[data-zone-card="${t.zoneId}"]`);
        if (!card) return;

        setCellValue(card, "[data-val-temp]", t.temperatureC.toFixed(1));
        setCellValue(card, "[data-val-hum]", t.humidityPercent.toFixed(1));
        setCellValue(card, "[data-val-co2]", Math.round(t.co2Ppm).toString());
        setCellValue(card, "[data-val-dp]", t.dewPointC.toFixed(1));
        setCellValue(card, "[data-val-vpd]", t.vpdKpa.toFixed(2));

        const velEl = card.querySelector("[data-val-vel]");
        if (velEl) {
            const sign = t.tempVelocityPerMin > 0 ? "+" : "";
            velEl.textContent = `${sign}${t.tempVelocityPerMin.toFixed(2)} °C/мин`;
            velEl.style.color = Math.abs(t.tempVelocityPerMin) >= 0.4
                ? "var(--agro-danger)"
                : "var(--agro-muted)";
        }

        const noteEl = card.querySelector("[data-val-note]");
        if (noteEl) noteEl.textContent = t.statusNote;

        const timeEl = card.querySelector("[data-val-time]");
        if (timeEl && t.recordedAtUtc) {
            const dt = new Date(t.recordedAtUtc);
            timeEl.textContent = dt.toLocaleTimeString("ru-RU");
        }

        card.classList.remove("mc-card-critical", "mc-card-warning", "mc-card-offline");
        const dot = card.querySelector("[data-zone-dot]");
        const badge = card.querySelector("[data-zone-badge]");

        if (!t.isSensorOnline || t.complianceState === "Offline") {
            card.classList.add("mc-card-offline");
            if (dot) dot.className = "status-dot offline";
            if (badge) {
                badge.className = "agro-badge agro-badge-danger";
                badge.textContent = "Обрыв связи";
            }
        } else if (t.complianceState === "Critical") {
            card.classList.add("mc-card-critical");
            if (dot) dot.className = "status-dot critical";
            if (badge) {
                badge.className = "agro-badge agro-badge-danger";
                badge.textContent = "Критическая тревога";
            }
        } else if (t.complianceState === "Warning") {
            card.classList.add("mc-card-warning");
            if (dot) dot.className = "status-dot warning";
            if (badge) {
                badge.className = "agro-badge agro-badge-warn";
                badge.textContent = t.graceRemainingSeconds
                    ? `Grace: ${t.graceRemainingSeconds} с`
                    : "Отклонение / тренд";
            }
        } else {
            if (dot) dot.className = "status-dot optimal";
            if (badge) {
                badge.className = "agro-badge";
                badge.textContent = "В норме";
            }
        }

        const warBtnSlot = card.querySelector("[data-warroom-slot]");
        if (warBtnSlot) {
            if (t.activeIncidentId) {
                warBtnSlot.innerHTML = `<a href="/Incidents/WarRoom/${t.activeIncidentId}" class="mc-btn mc-btn-danger mc-btn-sm">War Room #${t.activeIncidentId}</a>`;
            } else {
                warBtnSlot.innerHTML = "";
            }
        }
    }

    function updateGlobalEmergencyBanner(ev) {
        const host = document.getElementById("dashboard-emergency-host");
        if (!host) return;
        host.innerHTML = `
            <div class="emergency-strip">
                <div style="display:flex;align-items:center;gap:0.8rem;">
                    <span class="status-dot critical"></span>
                    <div>
                        <div style="font-weight:800;color:var(--agro-danger);">Активная тревога ${ev.incidentNumber} — ${ev.zoneName}</div>
                        <div style="font-size:0.86rem;">${ev.message}</div>
                    </div>
                </div>
                <a href="/Incidents/WarRoom/${ev.incidentId}" class="mc-btn mc-btn-danger">Открыть Incident War Room →</a>
            </div>`;
    }
})();
