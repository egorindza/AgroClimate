(function () {
    "use strict";

    // 1. Управление темой Mission Control (Dark / Light)
    window.toggleMcTheme = function () {
        const html = document.documentElement;
        const current = html.getAttribute("data-theme") || "dark";
        const next = current === "dark" ? "light" : "dark";
        html.setAttribute("data-theme", next);
        localStorage.setItem("agroclimate-theme", next);
        updateThemeToggleLabel(next);
        window.dispatchEvent(new CustomEvent("mc-theme-changed", { detail: { theme: next } }));
    };

    function updateThemeToggleLabel(theme) {
        const btn = document.getElementById("mc-theme-btn-label");
        if (btn) {
            btn.textContent = theme === "dark" ? "☀ Светлая" : "☾ Темная";
        }
    }

    document.addEventListener("DOMContentLoaded", function () {
        const current = document.documentElement.getAttribute("data-theme") || "dark";
        updateThemeToggleLabel(current);
        initSignalR();
    });

    function showToast(title, body, warRoomUrl, isCritical) {
        const container = document.getElementById("mc-toast-container");
        if (!container) return;

        const el = document.createElement("div");
        el.className = "mc-toast";
        el.style.borderLeftColor = isCritical ? "var(--accent-red)" : "var(--accent-amber)";

        const header = document.createElement("div");
        header.style.fontWeight = "700";
        header.style.marginBottom = "0.25rem";
        header.textContent = title;

        const msg = document.createElement("div");
        msg.style.color = "var(--text-secondary)";
        msg.textContent = body;

        el.appendChild(header);
        el.appendChild(msg);

        if (warRoomUrl) {
            const linkWrap = document.createElement("div");
            linkWrap.style.marginTop = "0.45rem";
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

    // 2. Живой поток телеметрии и событий War Room по SignalR
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
            if (connDot) {
                connDot.className = online ? "status-dot optimal" : "status-dot warning";
            }
            if (connText) {
                connText.textContent = online ? "SignalR Live" : "Reconnecting...";
            }
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
                    `🚨 ${ev.incidentNumber}: ${ev.zoneName}`,
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
                    connection.invoke("JoinIncidentWarRoom", parseInt(warRoomEl.dataset.incidentId, 10)).catch(() => {});
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
                ? "var(--accent-red)"
                : "var(--text-secondary)";
        }

        const noteEl = card.querySelector("[data-val-note]");
        if (noteEl) noteEl.textContent = t.statusNote;

        const timeEl = card.querySelector("[data-val-time]");
        if (timeEl && t.recordedAtUtc) {
            const dt = new Date(t.recordedAtUtc);
            timeEl.textContent = dt.toLocaleTimeString("ru-RU");
        }

        // Обновляем класс рамки и бейдж статуса
        card.classList.remove("mc-card-critical", "mc-card-warning", "mc-card-offline");
        const dot = card.querySelector("[data-zone-dot]");
        const badge = card.querySelector("[data-zone-badge]");

        if (!t.isSensorOnline || t.complianceState === "Offline") {
            card.classList.add("mc-card-offline");
            if (dot) dot.className = "status-dot offline";
            if (badge) {
                badge.className = "mc-badge mc-badge-critical";
                badge.textContent = "ОБРЫВ СВЯЗИ";
            }
        } else if (t.complianceState === "Critical") {
            card.classList.add("mc-card-critical");
            if (dot) dot.className = "status-dot critical";
            if (badge) {
                badge.className = "mc-badge mc-badge-critical";
                badge.textContent = "КРИТИЧЕСКАЯ ТРЕВОГА";
            }
        } else if (t.complianceState === "Warning") {
            card.classList.add("mc-card-warning");
            if (dot) dot.className = "status-dot warning";
            if (badge) {
                badge.className = "mc-badge mc-badge-warning";
                badge.textContent = t.graceRemainingSeconds
                    ? `GRACE PERIOD: ${t.graceRemainingSeconds} С`
                    : "ОТКЛОНЕНИЕ / ТРЕНД";
            }
        } else {
            if (dot) dot.className = "status-dot optimal";
            if (badge) {
                badge.className = "mc-badge mc-badge-optimal";
                badge.textContent = "В НОРМЕ";
            }
        }

        // Живая кнопка быстрого входа в War Room при открытии инцидента
        const warBtnSlot = card.querySelector("[data-warroom-slot]");
        if (warBtnSlot) {
            if (t.activeIncidentId) {
                warBtnSlot.innerHTML = `<a href="/Incidents/WarRoom/${t.activeIncidentId}" class="mc-btn mc-btn-danger mc-btn-sm">🚨 War Room #${t.activeIncidentId}</a>`;
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
                <div style="display:flex;align-items:center;gap:0.75rem;">
                    <span class="status-dot critical"></span>
                    <div>
                        <div style="font-weight:800;color:var(--accent-red);">АКТИВНАЯ ТРЕВОГА ${ev.incidentNumber} — ${ev.zoneName}</div>
                        <div style="font-size:0.84rem;color:var(--text-primary);">${ev.message}</div>
                    </div>
                </div>
                <a href="/Incidents/WarRoom/${ev.incidentId}" class="mc-btn mc-btn-danger">Перейти в Incident War Room →</a>
            </div>`;
    }
})();
