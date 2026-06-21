namespace NeoOrder.OneGate.Pages;

partial class LaunchDAppPage
{
    string CreateDocumentStartScript()
    {
        return CreateDapiInjectionScript() + CreateDAppDisplayAdapterScript();
    }

    static string CreateDAppDisplayAdapterScript()
    {
        return """
            (function () {
                const host = location.hostname.toLowerCase();
                if (host !== "governance.neo.org" && !host.endsWith(".governance.neo.org")) return;

                const css = `
                    @media (max-width: 820px) {
                        html.onegate-n3-governance,
                        html.onegate-n3-governance body,
                        html.onegate-n3-governance #app {
                            min-width: 0 !important;
                            width: 100% !important;
                            overflow-x: hidden !important;
                        }

                        html.onegate-n3-governance .overview {
                            box-sizing: border-box !important;
                            padding: 16px 20px 0 !important;
                            margin-top: 0 !important;
                            overflow: hidden !important;
                        }

                        html.onegate-n3-governance .overview .table-container {
                            box-sizing: border-box !important;
                            width: 100% !important;
                            overflow: visible !important;
                            padding: 0 !important;
                        }

                        html.onegate-n3-governance .overview .card-table,
                        html.onegate-n3-governance .overview .card-table tbody,
                        html.onegate-n3-governance .overview .card-table tr {
                            display: block !important;
                            width: 100% !important;
                            margin: 0 !important;
                            border-spacing: 0 !important;
                            border-collapse: collapse !important;
                            transform: none !important;
                        }

                        html.onegate-n3-governance .overview .card-table td {
                            display: block !important;
                            box-sizing: border-box !important;
                            width: 100% !important;
                            margin: 0 0 10px !important;
                            padding: 0 !important;
                        }

                        html.onegate-n3-governance .overview .info-card,
                        html.onegate-n3-governance .overview .remaining-card {
                            box-sizing: border-box !important;
                            width: 100% !important;
                            min-height: 72px !important;
                            height: auto !important;
                            border-radius: 8px !important;
                        }

                        html.onegate-n3-governance .overview .remaining-row {
                            display: flex !important;
                            flex-direction: column !important;
                            gap: 10px !important;
                            width: 100% !important;
                        }

                        html.onegate-n3-governance .overview .remaining-intro-wrapper,
                        html.onegate-n3-governance .overview .remaining-card {
                            width: 100% !important;
                        }

                        html.onegate-n3-governance .overview .remaining-intro-wrapper {
                            height: auto !important;
                            min-height: 72px !important;
                        }

                        html.onegate-n3-governance .overview .remaining-icon-wrapper {
                            width: 64px !important;
                            min-width: 64px !important;
                            height: 64px !important;
                        }

                        html.onegate-n3-governance .overview .remaining-intro {
                            font-size: 14px !important;
                            line-height: 18px !important;
                        }

                        html.onegate-n3-governance .overview .remaining-intro-title {
                            font-size: 16px !important;
                            line-height: 20px !important;
                        }

                        html.onegate-n3-governance .tabs .el-tabs__nav-scroll {
                            overflow-x: visible !important;
                        }

                        html.onegate-n3-governance .tabs .el-tabs__nav {
                            display: grid !important;
                            grid-template-columns: repeat(3, minmax(0, 1fr)) !important;
                            min-width: 100% !important;
                            width: 100% !important;
                            white-space: normal !important;
                            transform: none !important;
                        }

                        html.onegate-n3-governance .tabs .el-tabs__item {
                            display: flex !important;
                            align-items: center !important;
                            justify-content: center !important;
                            box-sizing: border-box !important;
                            min-width: 0 !important;
                            height: 44px !important;
                            margin: 8px 0 !important;
                            padding: 0 4px !important;
                            text-align: center !important;
                            white-space: normal !important;
                            font-size: 14px !important;
                            line-height: 17px !important;
                            letter-spacing: 0 !important;
                        }

                        html.onegate-n3-governance .tabs .table-container {
                            height: auto !important;
                            overflow: visible !important;
                        }

                        html.onegate-n3-governance .candidates {
                            box-sizing: border-box !important;
                            padding: 0 20px !important;
                        }

                        html.onegate-n3-governance .candidates-table,
                        html.onegate-n3-governance .candidates-table tbody {
                            display: block !important;
                            width: 100% !important;
                        }

                        html.onegate-n3-governance .candidates-table thead {
                            display: none !important;
                        }

                        html.onegate-n3-governance .candidates-table tr {
                            display: block !important;
                            box-sizing: border-box !important;
                            width: 100% !important;
                            margin: 0 0 12px !important;
                            padding: 12px 14px !important;
                            border: 1px solid #e4e7ec !important;
                            border-radius: 8px !important;
                            background: #ffffff !important;
                            box-shadow: 0 1px 0 rgba(20, 20, 66, 0.04) !important;
                        }

                        html.onegate-n3-governance .candidates-table td {
                            display: grid !important;
                            grid-template-columns: minmax(84px, 38%) minmax(0, 1fr) !important;
                            align-items: center !important;
                            column-gap: 12px !important;
                            box-sizing: border-box !important;
                            width: 100% !important;
                            min-height: 30px !important;
                            padding: 7px 0 !important;
                            border: 0 !important;
                            text-align: right !important;
                            font-size: 14px !important;
                            line-height: 18px !important;
                            letter-spacing: 0 !important;
                            white-space: normal !important;
                        }

                        html.onegate-n3-governance .candidates-table td::before {
                            content: attr(data-onegate-label);
                            justify-self: start;
                            color: #606380;
                            font-size: 12px;
                            line-height: 16px;
                            font-weight: 700;
                            text-align: left;
                            text-transform: uppercase;
                        }

                        html.onegate-n3-governance .candidates-table td[data-onegate-label=""]::before {
                            content: "";
                        }

                        html.onegate-n3-governance .candidates-table td:first-child {
                            grid-template-columns: 1fr !important;
                            padding-top: 0 !important;
                            text-align: left !important;
                        }

                        html.onegate-n3-governance .candidates-table td:first-child::before {
                            display: none !important;
                        }

                        html.onegate-n3-governance .candidate-status-wrapper {
                            height: auto !important;
                            min-height: 24px !important;
                        }

                        html.onegate-n3-governance .candidate-status-icon {
                            margin: 0 8px 0 0 !important;
                        }

                        html.onegate-n3-governance .candidate-name-wrapper,
                        html.onegate-n3-governance .candidate-hash-wrapper {
                            justify-content: flex-end !important;
                            min-width: 0 !important;
                            max-width: 100% !important;
                            overflow: hidden !important;
                        }

                        html.onegate-n3-governance .candidate-name-wrapper {
                            font-size: 14px !important;
                            line-height: 18px !important;
                        }

                        html.onegate-n3-governance .candidate-hash {
                            display: block !important;
                            max-width: 100% !important;
                            overflow: hidden !important;
                            text-overflow: ellipsis !important;
                            white-space: nowrap !important;
                        }
                    }
                `;

                function setMobileViewport() {
                    let viewport = document.querySelector('meta[name="viewport"]');
                    if (!viewport) {
                        viewport = document.createElement("meta");
                        viewport.name = "viewport";
                        (document.head || document.documentElement).appendChild(viewport);
                    }
                    viewport.setAttribute("content", "width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no, viewport-fit=cover");
                }

                function installStyle() {
                    document.documentElement.classList.add("onegate-n3-governance");
                    setMobileViewport();

                    let style = document.getElementById("onegate-n3-governance-adapter");
                    if (!style) {
                        style = document.createElement("style");
                        style.id = "onegate-n3-governance-adapter";
                        (document.head || document.documentElement).appendChild(style);
                    }
                    if (style.textContent !== css)
                        style.textContent = css;
                }

                function normalizeLabel(text, fallback) {
                    text = (text || "").replace(/\s+/g, " ").trim();
                    return text || fallback;
                }

                function labelCandidateTables() {
                    document.querySelectorAll(".candidates-table").forEach(function (table) {
                        const headers = Array.from(table.querySelectorAll("thead th")).map(function (header, index) {
                            return normalizeLabel(header.textContent, ["Status", "Name", "Location", "Total Votes", "Vote %"][index] || "");
                        });

                        table.querySelectorAll("tbody tr").forEach(function (row) {
                            Array.from(row.children).forEach(function (cell, index) {
                                if (cell instanceof HTMLElement)
                                    cell.dataset.onegateLabel = headers[index] || "";
                            });
                        });
                    });
                }

                let scheduled = false;
                function applyAdapter() {
                    if (scheduled) return;
                    scheduled = true;
                    requestAnimationFrame(function () {
                        scheduled = false;
                        installStyle();
                        labelCandidateTables();
                    });
                }

                applyAdapter();
                document.addEventListener("DOMContentLoaded", applyAdapter, { once: true });
                window.addEventListener("load", applyAdapter, { once: true });
                window.addEventListener("resize", applyAdapter);

                if (document.documentElement) {
                    new MutationObserver(applyAdapter).observe(document.documentElement, {
                        childList: true,
                        subtree: true
                    });
                }
            })();
            """.ReplaceLineEndings("");
    }
}
