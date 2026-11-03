/* Caisse Réseau Madagascar — comportements côté navigateur (la logique métier reste sur le serveur). */
(function () {
  "use strict";

  var $ = function (selector, root) { return (root || document).querySelector(selector); };
  var $$ = function (selector, root) { return Array.prototype.slice.call((root || document).querySelectorAll(selector)); };

  var ICON_CHECK = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m5 12 4 4L19 6"/></svg>';
  var ICON_CHECK_SMALL = ICON_CHECK.replace('width="18" height="18"', 'width="11" height="11"');
  var ICON_WARN = '<svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3 2.5 20h19L12 3Z"/><path d="M12 9v4M12 17h.01"/></svg>';
  var ICON_OK = ICON_WARN.replace('<path d="M12 3 2.5 20h19L12 3Z"/><path d="M12 9v4M12 17h.01"/>', '<path d="m5 12 4 4L19 6"/>');
  var ICON_CHECK_12 = ICON_CHECK.replace('width="18" height="18"', 'width="12" height="12"');

  var COMPANIES = {
    ARBIOCHEM: { label: "ARBIOCHEM", prefix: "ARB" },
    ACTIVO: { label: "ACTIVO", prefix: "ACT" },
    FEED: { label: "FEED", prefix: "FED" },
    CAISSE_15: { label: "Caisse 15", prefix: "C15" },
    AVANCE_POUSSIN: { label: "Avance poussin", prefix: "AVP" },
    VENTE_INTERNE: { label: "Vente œuf et poulet en interne", prefix: "VOI" },
    VETCARE_CENTRALISE: { label: "Vetcare centralisé", prefix: "VTC" }
  };
  var CHANNELS = {
    ESPECE: "Espèces", ORANGE_MONEY: "Orange Money", MVOLA: "MVola", TPE: "TPE",
    VIREMENT_VERSEMENT: "Virement", CHEQUE: "Chèque", TRAITE: "Traite"
  };
  var NATURES = {
    AU_COMPTANT: "Au comptant", REGLEMENT: "Règlement", ACOMPTE: "Acompte client",
    VERSEMENT_DEGAGEMENT: "Versement / dégagement", TRANSFERT_ACOMPTE: "Transfert d’acompte"
  };

  // ------------------------------------------------------------------ utilitaires

  function number(value) {
    var negative = value < 0;
    var digits = String(Math.abs(Math.round(value)));
    var out = "";
    for (var i = 0; i < digits.length; i++) {
      if (i > 0 && (digits.length - i) % 3 === 0) out += " ";
      out += digits.charAt(i);
    }
    return (negative ? "-" : "") + out;
  }
  function money(value) { return number(value) + " Ar"; }

  function uuid() {
    if (window.crypto && window.crypto.randomUUID) return window.crypto.randomUUID();
    return "id-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10) + Math.random().toString(36).slice(2, 10);
  }

  function token() {
    var input = $("#antiforgery input[name=__RequestVerificationToken]");
    return input ? input.value : "";
  }

  function post(url, body) {
    var headers = { "RequestVerificationToken": token(), "X-Requested-With": "fetch" };
    var options = { method: "POST", headers: headers, credentials: "same-origin" };
    if (body !== undefined) {
      headers["Content-Type"] = "application/json";
      options.body = JSON.stringify(body);
    }
    return fetch(url, options).then(function (response) {
      if (response.status === 401) { window.location.reload(); throw new Error("Session expirée."); }
      return response.json().catch(function () { return {}; }).then(function (data) {
        if (!response.ok) throw new Error(data.error || "Opération impossible");
        return data;
      });
    });
  }

  function toast(message) {
    var host = $("#toast-host");
    if (!host) return;
    host.innerHTML = "";
    var node = document.createElement("div");
    node.className = "toast";
    var icon = document.createElement("span");
    icon.innerHTML = ICON_CHECK;
    node.appendChild(icon);
    node.appendChild(document.createTextNode(message));
    host.appendChild(node);
    window.setTimeout(function () { if (node.parentNode) node.parentNode.removeChild(node); }, 4000);
  }

  function flash(message) {
    try { window.sessionStorage.setItem("caisse.flash", message); } catch (e) { /* stockage indisponible */ }
  }

  function showError(box, textNode, message) {
    if (!box) return;
    if (!message) { box.hidden = true; return; }
    if (textNode) textNode.textContent = message; else box.textContent = message;
    box.hidden = false;
  }

  function hide(node, hidden) { if (node) node.hidden = !!hidden; }

  // ------------------------------------------------------------------ coque : navigation, agence, déconnexion

  function initShell() {
    document.addEventListener("click", function (event) {
      var nav = event.target.closest("[data-nav]");
      if (nav) { window.location.href = nav.getAttribute("data-nav"); return; }
      if (event.target.closest("[data-logout]")) { var form = $("#logout-form"); if (form) form.submit(); }
    });

    var sidebar = $("#sidebar");
    var backdrop = $("#nav-backdrop");
    var menu = $("#mobile-menu");
    function closeMenu() { if (sidebar) sidebar.classList.remove("is-open"); if (backdrop) backdrop.hidden = true; }
    if (menu) menu.addEventListener("click", function () { sidebar.classList.add("is-open"); backdrop.hidden = false; });
    if (backdrop) backdrop.addEventListener("click", closeMenu);

    var select = $("#agency-select");
    if (select && !select.disabled) {
      select.addEventListener("change", function () {
        var body = "code=" + encodeURIComponent(select.value);
        fetch("/Home/SelectAgency", {
          method: "POST",
          credentials: "same-origin",
          headers: { "Content-Type": "application/x-www-form-urlencoded", "RequestVerificationToken": token(), "X-Requested-With": "fetch" },
          body: body
        }).then(function () { window.location.reload(); });
      });
    }

    var serverToast = $("#server-toast");
    if (serverToast) window.setTimeout(function () { if (serverToast.parentNode) serverToast.parentNode.removeChild(serverToast); }, 4000);
    try {
      var message = window.sessionStorage.getItem("caisse.flash");
      if (message) { window.sessionStorage.removeItem("caisse.flash"); toast(message); }
    } catch (e) { /* stockage indisponible */ }
  }

  // ------------------------------------------------------------------ ouverture / relève de caisse

  function initSessionDialog() {
    var dialog = $("#session-dialog");
    if (!dialog) return;
    var form = $("#session-form");
    var error = $("#session-error");
    var cashier = $("#session-cashier");
    var useFunds = $("#session-use-funds");
    var funds = $("#session-funds");
    var submit = $("#session-submit");
    var exists = dialog.getAttribute("data-exists") === "1";

    function open() { dialog.hidden = false; showError(error, null, ""); if (cashier) cashier.focus(); }
    function close() { dialog.hidden = true; }

    document.addEventListener("click", function (event) {
      if (event.target.closest("#open-session") || event.target.closest("[data-open-session]")) open();
    });
    $("#session-cancel").addEventListener("click", close);
    if (useFunds) useFunds.addEventListener("change", function () { funds.hidden = !useFunds.checked; });

    form.addEventListener("submit", function (event) {
      event.preventDefault();
      showError(error, null, "");
      if (dialog.getAttribute("data-status") === "CLOSED") {
        showError(error, null, "Cette caisse est clôturée. Elle ne pourra être rouverte qu’à partir de 06 h demain.");
        return;
      }
      var name = (cashier.value || "").trim();
      if (name.length < 2) { showError(error, null, "Veuillez saisir le nom de la personne qui assure la caisse."); return; }
      var openingFunds = {};
      if (useFunds && useFunds.checked && !exists) {
        $$("[data-fund]", dialog).forEach(function (input) {
          openingFunds[input.getAttribute("data-fund")] = Math.max(0, Math.round(Number(input.value || 0)));
        });
      }
      submit.disabled = true;
      submit.textContent = "Enregistrement…";
      post("/Sessions/Open", { agencyCode: dialog.getAttribute("data-agency"), cashierName: name, openingFunds: openingFunds })
        .then(function (data) {
          flash(data.handover ? "Relève enregistrée : " + data.cashier + "." : "Caisse ouverte : " + data.cashier + ".");
          window.location.reload();
        })
        .catch(function (e) {
          showError(error, null, e.message);
          submit.disabled = false;
          submit.textContent = exists ? "Confirmer la relève" : "Ouvrir la caisse";
        });
    });
  }

  // ------------------------------------------------------------------ aperçu / impression du reçu

  function showReceipt(html, ticket) {
    var host = $("#receipt-host");
    if (!host) return;
    host.innerHTML =
      '<div class="receipt-preview-backdrop"><section class="receipt-preview-dialog" role="dialog" aria-modal="true" aria-labelledby="receipt-title">' +
      '<header><div><h2 id="receipt-title"></h2><p>Sauvegarde confirmée.</p></div><button type="button" class="secondary-button" data-receipt-close>Fermer</button></header>' +
      '<iframe sandbox="allow-same-origin allow-modals"></iframe>' +
      '<footer><p>Sélectionnez l’imprimante XP-80T et le papier 80 mm. Si la fenêtre d’impression ne s’ouvre pas, cliquez sur « Imprimer le reçu ».</p><p role="alert" data-receipt-error hidden></p>' +
      '<button type="button" class="primary-button" data-receipt-print disabled>Imprimer le reçu</button></footer></section></div>';
    $("#receipt-title", host).textContent = "Reçu " + ticket;
    var frame = $("iframe", host);
    frame.title = "Aperçu du reçu " + ticket;
    var printButton = $("[data-receipt-print]", host);
    var errorNode = $("[data-receipt-error]", host);

    function sendToPrinter() {
      try {
        var target = frame.contentWindow;
        if (!target) throw new Error("L’aperçu du reçu est en cours de chargement. Réessayez dans un instant.");
        target.focus();
        target.print();
        errorNode.hidden = true;
      } catch (e) {
        errorNode.textContent = e.message || "Impression indisponible. Réessayez avec le bouton Imprimer.";
        errorNode.hidden = false;
      }
    }
    frame.addEventListener("load", function () { printButton.disabled = false; sendToPrinter(); });
    frame.srcdoc = html;
    printButton.addEventListener("click", sendToPrinter);
    $("[data-receipt-close]", host).addEventListener("click", function () { host.innerHTML = ""; });
  }

  function fetchReceipt(url, id) {
    return fetch(url + "?id=" + encodeURIComponent(id), { credentials: "same-origin", headers: { "X-Requested-With": "fetch" } })
      .then(function (response) {
        if (!response.ok) throw new Error("Reçu introuvable.");
        return response.text();
      });
  }

  // ------------------------------------------------------------------ journal

  function csvCell(value) {
    var text = String(value === null || value === undefined ? "" : value);
    return '"' + text.replace(/"/g, '""') + '"';
  }

  function initJournal() {
    var root = $("#journal-view");
    if (!root) return;
    var rows = $$("#journal-body tr");
    var channel = "ALL";

    function apply() {
      var search = ($("#journal-search").value || "").toLowerCase();
      var sage = $("#journal-sage").value;
      var shown = 0;
      rows.forEach(function (row) {
        var ok = (channel === "ALL" || row.getAttribute("data-channel") === channel)
          && (sage === "ALL" || row.getAttribute("data-sage") === sage)
          && row.getAttribute("data-search").indexOf(search) !== -1;
        row.hidden = !ok;
        if (ok) shown++;
      });
      $("#journal-count").textContent = shown + " ligne(s)";
      $("#journal-empty").hidden = shown > 0;
    }

    $$("[data-channel-tab]").forEach(function (tab) {
      tab.addEventListener("click", function () {
        channel = tab.getAttribute("data-channel-tab");
        $$("[data-channel-tab]").forEach(function (other) { other.classList.toggle("selected", other === tab); });
        apply();
      });
    });
    $("#journal-search").addEventListener("input", apply);
    $("#journal-sage").addEventListener("change", apply);

    $("#journal-export").addEventListener("click", function () {
      var header = ["DATE", "AGENCE", "CAISSIER", "SOCIETE", "MODE", "NATURE", "TICKET", "FACTURE", "CLIENT", "ENTREE", "SORTIE", "STATUT SAGE", "IMPRESSION"];
      var lines = [header.map(csvCell).join(";")];
      rows.filter(function (row) { return !row.hidden; }).forEach(function (row) {
        lines.push(JSON.parse(row.getAttribute("data-csv")).map(csvCell).join(";"));
      });
      var blob = new Blob(["﻿", lines.join("\r\n")], { type: "text/csv;charset=utf-8" });
      var url = URL.createObjectURL(blob);
      var anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = "journal-caisse-" + new Date().toISOString().slice(0, 10) + ".csv";
      anchor.click();
      URL.revokeObjectURL(url);
    });

    // Imputation SAGE
    document.addEventListener("click", function (event) {
      var button = event.target.closest("[data-impute]");
      if (!button) return;
      button.disabled = true;
      button.textContent = "Mise à jour…";
      post(root.getAttribute("data-impute-url"), { id: Number(button.getAttribute("data-impute")) })
        .then(function () {
          var cell = button.parentNode;
          cell.innerHTML = '<span class="status-badge sage-ok">' + ICON_CHECK_12 + ' Imputé</span>';
          var row = cell.parentNode;
          row.setAttribute("data-sage", "Imputé");
          apply();
        })
        .catch(function (e) { button.disabled = false; button.textContent = "Marquer imputé"; toast(e.message); });
    });

    // Impression / duplicata
    var printing = false;
    document.addEventListener("click", function (event) {
      var button = event.target.closest("[data-print]");
      if (!button || printing) return;
      printing = true;
      var label = $("[data-print-label]", button);
      var previous = label.textContent;
      label.textContent = "Préparation…";
      button.disabled = true;
      var id = Number(button.getAttribute("data-print"));
      post(root.getAttribute("data-record-url"), { id: id, clientPrintId: "print-" + uuid() })
        .then(function (data) {
          return fetchReceipt(root.getAttribute("data-receipt-url"), id).then(function (html) {
            var ticket = (html.match(/<title>([^<]*)<\/title>/) || [])[1] || "";
            button.setAttribute("data-print-count", data.printCount);
            $$('[data-print="' + id + '"] [data-print-label]').forEach(function (node) { node.textContent = "Réimprimer (duplicata)"; });
            var row = $('tr[data-id="' + id + '"]');
            if (row) {
              var small = $(".ticket-cell small", row);
              if (small) small.textContent = small.textContent.replace(/ · .*$/, " · " + data.printCount + " impression(s)");
            }
            toast("Reçu prêt à imprimer en un exemplaire.");
            showReceipt(html, ticket);
          });
        })
        .catch(function (e) { label.textContent = previous; toast(e.message); })
        .then(function () { printing = false; button.disabled = false; $$("[data-print-label]", button).forEach(function (n) { if (n.textContent === "Préparation…") n.textContent = "Réimprimer (duplicata)"; }); });
    });

    // Aperçu automatique après création d'un ticket
    var auto = root.getAttribute("data-auto-print");
    if (auto) {
      fetchReceipt(root.getAttribute("data-receipt-url"), auto).then(function (html) {
        var ticket = (html.match(/<title>([^<]*)<\/title>/) || [])[1] || "";
        showReceipt(html, ticket);
        toast("Reçu prêt à imprimer en un exemplaire.");
      }).catch(function () { /* l'utilisateur peut imprimer depuis le bandeau */ });
    }

    initDeposit(root);
  }

  function initDeposit(root) {
    var dialog = $("#deposit-dialog");
    if (!dialog) return;
    var current = null;
    var error = $("#dep-error");
    var companySelect = $("#dep-company");

    function updateNote() {
      if (!current) return;
      $("#dep-note").textContent = "une sortie interne de " + current.company + " et une entrée correspondante dans la société bénéficiaire seront créées et liées au ticket d’origine.";
    }

    document.addEventListener("click", function (event) {
      var button = event.target.closest("[data-deposit]");
      if (!button) return;
      current = {
        id: Number(button.getAttribute("data-deposit")),
        ticket: button.getAttribute("data-ticket"),
        customer: button.getAttribute("data-customer"),
        company: button.getAttribute("data-company"),
        remaining: Number(button.getAttribute("data-remaining")),
        total: Number(button.getAttribute("data-total"))
      };
      $("#dep-ticket").textContent = current.ticket;
      $("#dep-customer").textContent = current.customer;
      $("#dep-available").textContent = "Disponible : " + money(current.remaining) + " sur " + money(current.total);
      $("#dep-amount").value = String(current.remaining);
      $("#dep-amount").max = String(current.remaining);
      $("#dep-invoice").value = "";
      showError(error, null, "");
      updateNote();
      dialog.hidden = false;
      $("#dep-invoice").focus();
    });
    $("#dep-cancel").addEventListener("click", function () { dialog.hidden = true; });

    $("#deposit-form").addEventListener("submit", function (event) {
      event.preventDefault();
      if (!current) {
        showError(error, null, "Sélectionnez d’abord un acompte dans le journal.");
        return;
      }
      var amount = Math.round(Number($("#dep-amount").value));
      var invoice = ($("#dep-invoice").value || "").trim();
      if (!isFinite(amount) || amount <= 0 || amount > current.remaining || !invoice) {
        showError(error, null, "Indiquez une facture et un montant compris dans le solde disponible.");
        return;
      }
      var submit = $("#dep-submit");
      submit.disabled = true;
      submit.textContent = "Affectation…";
      post(root.getAttribute("data-allocate-url"), { id: current.id, targetCompany: companySelect.value, invoiceNumber: invoice, amount: amount })
        .then(function () { flash("Acompte affecté : transfert et règlement créés."); window.location.reload(); })
        .catch(function (e) { showError(error, null, e.message); submit.disabled = false; submit.textContent = "Affecter l’acompte"; });
    });
  }

  // ------------------------------------------------------------------ saisie d'une opération

  function initEntry() {
    var root = $("#entry-view");
    if (!root) return;
    var form = $("#entry-form");
    var agency = root.getAttribute("data-agency");
    var today = root.getAttribute("data-today");
    var state = { channel: "ESPECE", direction: "ENTREE", nature: "REGLEMENT", fr: false };
    var clientOperationId = "op-" + uuid();
    var saving = false;

    var el = {
      company: $("#f-company"), date: $("#f-date"), direction: $("#f-direction"), invoice: $("#f-invoice"),
      customer: $("#f-customer"), amount: $("#f-amount"), reference: $("#f-reference"), bank: $("#f-bank"),
      document: $("#f-document"), maturity: $("#f-maturity"), notes: $("#f-notes"), fr: $("#f-fr")
    };

    function isFr(value) { return /^FR[A-Z]{0,5}[-/ ]?[0-9]/i.test((value || "").trim()); }

    function refresh() {
      var documentChannel = state.channel === "CHEQUE" || state.channel === "TRAITE";
      $$("#channel-picker button").forEach(function (button) {
        var selected = button.getAttribute("data-channel") === state.channel;
        button.classList.toggle("selected", selected);
        var mark = $("i", button);
        if (selected && !mark) { mark = document.createElement("i"); mark.innerHTML = ICON_CHECK_SMALL; button.appendChild(mark); }
        if (!selected && mark) button.removeChild(mark);
      });
      $("#opt-sortie").disabled = documentChannel;

      var entry = state.direction === "ENTREE";
      hide($("#nature-buttons"), !entry);
      hide($("#nature-fixed"), entry);
      $$("#nature-buttons button").forEach(function (button) {
        button.classList.toggle("selected", button.getAttribute("data-nature") === state.nature);
      });

      var invoiceRequired = entry && state.nature !== "ACOMPTE";
      $("#invoice-star").textContent = invoiceRequired ? "*" : "";
      el.invoice.placeholder = state.nature === "ACOMPTE" ? "Facultatif lors du dépôt" : "Ex. FAC-2026-001";

      hide($("#wrap-reference"), state.channel === "ESPECE");
      hide($("#wrap-bank"), !documentChannel);
      hide($("#wrap-document"), !documentChannel);
      hide($("#wrap-maturity"), !documentChannel);
      $("#maturity-star").textContent = state.channel === "TRAITE" ? "*" : "";
      el.bank.required = documentChannel;
      el.document.required = documentChannel;
      el.maturity.required = documentChannel;

      hide($("#wrap-fr"), entry);
      var recognized = isFr(el.invoice.value);
      el.fr.checked = state.fr || recognized;
      el.fr.disabled = recognized;
      $("#fr-hint").textContent = (recognized ? "FR reconnue par la référence de facture." : "À cocher pour une FR saisie avec la référence d’origine.") +
        " Une FR concernant une journée déjà validée sera conservée pour l’audit, sans bloquer la caisse.";

      var company = COMPANIES[el.company.value] || { label: el.company.value, prefix: "SOC" };
      var year = (el.date.value || today).slice(0, 4);
      $("#pv-ticket").textContent = company.prefix + "-" + agency + "-" + year + " · attribué à l’impression";
      $("#pv-company").textContent = company.label;
      $("#pv-customer").textContent = el.customer.value || "—";
      $("#pv-channel").textContent = CHANNELS[state.channel];
      $("#pv-nature").textContent = NATURES[state.nature];
      $("#pv-invoice").textContent = el.invoice.value || "—";
      $("#pv-amount").textContent = money(Math.round(Number(el.amount.value || 0)));
    }

    $("#channel-picker").addEventListener("click", function (event) {
      var button = event.target.closest("[data-channel]");
      if (!button) return;
      state.channel = button.getAttribute("data-channel");
      refresh();
    });
    $("#nature-buttons").addEventListener("click", function (event) {
      var button = event.target.closest("[data-nature]");
      if (!button) return;
      state.nature = button.getAttribute("data-nature");
      refresh();
    });
    el.direction.addEventListener("change", function () {
      state.direction = el.direction.value;
      state.nature = state.direction === "SORTIE" ? "VERSEMENT_DEGAGEMENT" : "REGLEMENT";
      if (state.direction !== "SORTIE") state.fr = false;
      refresh();
    });
    el.fr.addEventListener("change", function () { state.fr = el.fr.checked; refresh(); });
    ["company", "date", "invoice", "customer", "amount", "reference"].forEach(function (key) {
      el[key].addEventListener("input", refresh);
      el[key].addEventListener("change", refresh);
    });

    var errorBox = $("#entry-error");
    var errorText = $("#entry-error-text");

    form.addEventListener("submit", function (event) {
      event.preventDefault();
      if (saving) return;
      showError(errorBox, errorText, "");
      var documentChannel = state.channel === "CHEQUE" || state.channel === "TRAITE";
      var amount = Math.round(Number(el.amount.value || 0));

      if (root.getAttribute("data-open") !== "1") {
        showError(errorBox, errorText, root.getAttribute("data-session-status") === "CLOSED"
          ? "Cette caisse est déjà fermée. Réouverture demain à partir de 06 h."
          : "Ouvrez d’abord la caisse et renseignez le caissier en poste.");
        return;
      }
      if (el.date.value !== today) { showError(errorBox, errorText, "Les nouvelles opérations doivent être saisies dans la journée de caisse ouverte."); return; }
      if (!el.customer.value.trim() || amount <= 0 || (state.direction === "ENTREE" && state.nature !== "ACOMPTE" && !el.invoice.value.trim())) {
        showError(errorBox, errorText, "Renseignez le client, un montant valide et la facture, sauf pour un acompte.");
        return;
      }
      if (state.channel !== "ESPECE" && !el.reference.value.trim()) { showError(errorBox, errorText, "La référence de paiement est obligatoire."); return; }
      if (documentChannel && (!el.document.value.trim() || !el.maturity.value)) {
        showError(errorBox, errorText, "Le numéro du document et son échéance sont obligatoires.");
        return;
      }

      var fr = state.direction === "SORTIE" && (state.fr || isFr(el.invoice.value));
      var notes = el.notes.value;
      if (fr && !/^\[FR\](?:\s|$)/i.test(notes)) notes = ("[FR] " + notes).trim();

      saving = true;
      var submit = $("#entry-submit");
      submit.disabled = true;
      $("#entry-submit-label").textContent = "Enregistrement…";

      post(root.getAttribute("data-create-url"), {
        agencyCode: agency,
        company: el.company.value,
        channel: state.channel,
        direction: state.direction,
        transactionNature: state.nature,
        transactionDate: el.date.value,
        invoiceNumber: el.invoice.value,
        customerName: el.customer.value,
        amount: amount,
        reference: el.reference.value,
        bankName: el.bank.value,
        documentNumber: el.document.value,
        maturityDate: el.maturity.value || null,
        notes: notes,
        auditCategory: fr ? "FR" : null,
        clientOperationId: clientOperationId
      }).then(function (data) {
        window.location.href = root.getAttribute("data-journal-url") + "?ticket=" + encodeURIComponent(data.id);
      }).catch(function (e) {
        saving = false;
        submit.disabled = false;
        $("#entry-submit-label").textContent = "Enregistrer et imprimer";
        showError(errorBox, errorText, e.message || "Enregistrement impossible");
      });
    });

    refresh();
  }

  // ------------------------------------------------------------------ comptage (clôture et régularisation)

  function initCount(root, options) {
    var theoretical = Number(root.getAttribute("data-theoretical") || 0);
    var inputs = $$("[data-denom]", root);
    if (!inputs.length) return { counts: function () { return {}; }, counted: function () { return 0; } };

    function counts() {
      var result = {};
      inputs.forEach(function (input) {
        var value = Math.max(0, Math.round(Number(input.value || 0)));
        if (value > 0) result[input.getAttribute("data-denom")] = value;
      });
      return result;
    }
    function counted() {
      return inputs.reduce(function (sum, input) {
        return sum + Number(input.getAttribute("data-denom")) * Math.max(0, Math.round(Number(input.value || 0)));
      }, 0);
    }
    function refresh() {
      var total = counted();
      var variance = total - theoretical;
      inputs.forEach(function (input) {
        var label = $('[data-denom-total="' + input.getAttribute("data-denom") + '"]', root);
        if (label) label.textContent = "= " + money(Number(input.getAttribute("data-denom")) * Math.max(0, Math.round(Number(input.value || 0))));
      });
      $("#counted-total", root).textContent = money(total);
      var panel = $("#variance-panel", root);
      panel.classList.toggle("balanced", variance === 0);
      panel.classList.toggle("has-variance", variance !== 0);
      $("#var-icon", root).innerHTML = variance === 0 ? ICON_OK : ICON_WARN;
      $("#var-value", root).textContent = (variance > 0 ? "+" : "") + money(variance);
      $("#var-text", root).textContent = options.varianceText(variance);
      var star = $("#comment-star", root);
      if (star) star.textContent = variance !== 0 ? "*" : "";
    }
    inputs.forEach(function (input) { input.addEventListener("input", refresh); });
    refresh();
    return { counts: counts, counted: counted, variance: function () { return counted() - theoretical; } };
  }

  function madagascarWindow() {
    var d = new Date(Date.now() + 3 * 3600 * 1000);
    var minutes = d.getUTCHours() * 60 + d.getUTCMinutes();
    if (minutes < 11 * 60) return "BEFORE";
    if (minutes <= 18 * 60 + 30) return "OPEN";
    return "AFTER";
  }

  function initClosure() {
    var root = $("#closure-view");
    if (!root) return;
    var count = initCount(root, {
      varianceText: function (variance) { return variance === 0 ? "Le comptage correspond à la caisse théorique." : "Un commentaire est obligatoire."; }
    });
    var errorBox = $("#closure-error");
    var errorText = $("#closure-error-text");
    var agency = root.getAttribute("data-agency");
    var company = root.getAttribute("data-company");

    window.setInterval(function () {
      if (madagascarWindow() !== root.getAttribute("data-window")) window.location.reload();
    }, 30000);

    $("#closure-form").addEventListener("submit", function (event) {
      event.preventDefault();
      showError(errorBox, errorText, "");
      if (root.getAttribute("data-can-submit") !== "1") { showError(errorBox, errorText, root.getAttribute("data-window-message")); return; }
      if (madagascarWindow() !== "OPEN") { showError(errorBox, errorText, root.getAttribute("data-window-message")); return; }
      var comment = ($("#closure-comment").value || "").trim();
      if (count.variance() !== 0 && !comment) { showError(errorBox, errorText, "Justifiez l’écart avant de clôturer cette société."); return; }
      var submit = $("#closure-submit");
      submit.disabled = true;
      $("span", submit).textContent = "Clôture…";
      post(root.getAttribute("data-close-url"), { agencyCode: agency, company: company, cashCounted: count.counted(), breakdown: count.counts(), comment: comment })
        .then(function () { flash("Société clôturée."); window.location.reload(); })
        .catch(function (e) { submit.disabled = false; $("span", submit).textContent = "Clôturer"; showError(errorBox, errorText, e.message || "Clôture impossible"); });
    });

    $("#closure-finalize").addEventListener("click", function () {
      showError(errorBox, errorText, "");
      if (madagascarWindow() !== "OPEN") { showError(errorBox, errorText, root.getAttribute("data-window-message")); return; }
      var button = $("#closure-finalize");
      button.disabled = true;
      button.textContent = "Fermeture…";
      post(root.getAttribute("data-finalize-url"), { agencyCode: agency })
        .then(function () { flash("Caisse du jour fermée."); window.location.reload(); })
        .catch(function (e) { button.disabled = false; button.textContent = "Fermer la caisse du jour"; showError(errorBox, errorText, e.message || "Fermeture impossible"); });
    });
  }

  function initRegularization() {
    var root = $("#regularization-view");
    if (!root) return;
    var select = $("#late-session-select");
    if (select) select.addEventListener("change", function () { window.location.href = select.getAttribute("data-base") + "?session=" + encodeURIComponent(select.value); });

    var existing = root.getAttribute("data-existing") === "1";
    var count = initCount(root, {
      varianceText: function (variance) {
        if (existing) return "Cette société a déjà été régularisée.";
        return variance === 0 ? "Le comptage correspond à la caisse théorique." : "L’écart sera conservé dans la piste d’audit.";
      }
    });
    var errorBox = $("#late-error");
    var errorText = $("#late-error-text");
    var agency = root.getAttribute("data-agency");
    var date = root.getAttribute("data-date");
    var company = root.getAttribute("data-company");
    function reason() { var node = $("#late-reason"); return node ? (node.value || "").trim() : ""; }

    var form = $("#regularization-form");
    form.addEventListener("submit", function (event) {
      event.preventDefault();
      if (existing) return;
      showError(errorBox, errorText, "");
      if (reason().length < 5) { showError(errorBox, errorText, "Indiquez le motif de la clôture tardive (au moins 5 caractères)."); return; }
      var submit = $("#late-submit");
      if (!submit) return;
      submit.disabled = true;
      $("span", submit).textContent = "Enregistrement…";
      post(root.getAttribute("data-close-url"), { agencyCode: agency, company: company, closureDate: date, cashCounted: count.counted(), breakdown: count.counts(), reason: reason() })
        .then(function () { flash("Société régularisée."); window.location.reload(); })
        .catch(function (e) { submit.disabled = false; $("span", submit).textContent = "Régulariser"; showError(errorBox, errorText, e.message || "Régularisation impossible"); });
    });

    var finalize = $("#late-finalize");
    if (finalize) finalize.addEventListener("click", function () {
      showError(errorBox, errorText, "");
      if (reason().length < 5) { showError(errorBox, errorText, "Indiquez le motif de la clôture tardive (au moins 5 caractères)."); return; }
      finalize.disabled = true;
      post(root.getAttribute("data-finalize-url"), { agencyCode: agency, businessDate: date, reason: reason() })
        .then(function () { flash("Journée régularisée et clôturée."); window.location.href = select ? select.getAttribute("data-base") : window.location.pathname; })
        .catch(function (e) { finalize.disabled = false; showError(errorBox, errorText, e.message || "Régularisation impossible"); });
    });
  }

  // ------------------------------------------------------------------ rapports

  function initReports() {
    var root = $("#reports-view");
    if (!root) return;
    var form = $("#report-form");
    $$("input, select", form).forEach(function (control) { control.addEventListener("change", function () { form.submit(); }); });
    $("#report-print").addEventListener("click", function () { window.print(); });
    $("#report-export").addEventListener("click", function () {
      var from = form.elements["from"].value;
      var to = form.elements["to"].value;
      if (from > to) { window.alert("La date de début doit précéder la date de fin."); return; }
      var agency = form.elements["agency"].value || "ALL";
      var button = $("#report-export");
      var previous = button.innerHTML;
      button.disabled = true;
      button.lastChild.textContent = " Création des fichiers…";
      window.location.href = root.getAttribute("data-export-url") + "?from=" + encodeURIComponent(from) + "&to=" + encodeURIComponent(to) + "&agency=" + encodeURIComponent(agency);
      window.setTimeout(function () { button.disabled = false; button.innerHTML = previous; }, 4000);
    });

    // Panneau FR
    var pieces = [];
    var scope = "";
    var loaded = false;
    function currentScope() { return ($("#report-form").elements["agency"].value || "ALL") + ":" + $("#fr-from").value + ":" + $("#fr-to").value; }
    function refreshExport() { $("#fr-export").disabled = !(loaded && scope === currentScope() && pieces.length); }
    ["#fr-from", "#fr-to"].forEach(function (selector) { $(selector).addEventListener("change", refreshExport); });

    $("#fr-load").addEventListener("click", function () {
      var button = $("#fr-load");
      var error = $("#fr-error");
      error.hidden = true;
      button.disabled = true;
      button.textContent = "Chargement…";
      var agency = form.elements["agency"].value || "ALL";
      fetch(root.getAttribute("data-fr-url") + "?agency=" + encodeURIComponent(agency) + "&from=" + $("#fr-from").value + "&to=" + $("#fr-to").value, { credentials: "same-origin", headers: { "X-Requested-With": "fetch" } })
        .then(function (response) { return response.json().then(function (data) { if (!response.ok) throw new Error(data.error || "Chargement impossible"); return data; }); })
        .then(function (data) {
          pieces = data.items;
          loaded = true;
          scope = agency + ":" + $("#fr-from").value + ":" + $("#fr-to").value;
          $("#fr-count").textContent = pieces.length + " pièce(s) à contrôler";
          var body = $("#fr-body");
          body.innerHTML = "";
          pieces.forEach(function (piece) {
            var tr = document.createElement("tr");
            var cells = [piece.agency, piece.date.split("-").reverse().join("/"), null, piece.company, piece.customer, money(piece.amount), piece.notes, piece.status];
            cells.forEach(function (value, index) {
              var td = document.createElement("td");
              if (index === 2) {
                td.appendChild(document.createTextNode(piece.ticket));
                var small = document.createElement("small");
                small.className = "nature-label";
                small.textContent = piece.invoice;
                td.appendChild(small);
              } else {
                td.textContent = value;
              }
              tr.appendChild(td);
            });
            body.appendChild(tr);
          });
          $("#fr-result").hidden = false;
          refreshExport();
        })
        .catch(function (e) { error.textContent = e.message || "Chargement impossible"; error.hidden = false; })
        .then(function () { button.disabled = false; button.textContent = "Afficher les FR"; });
    });

    $("#fr-export").addEventListener("click", function () {
      var rows = [["AGENCE", "DATE OPÉRATION", "TICKET", "SOCIÉTÉ", "CLIENT / LIBELLÉ", "FACTURE", "MONTANT (AR)", "OBSERVATION", "REÇUE LE", "STATUT"]];
      pieces.forEach(function (p) { rows.push([p.agency, p.date, p.ticket, p.company, p.customer, p.invoice, p.amount, p.notes, p.receivedAt, p.status]); });
      var csv = "﻿" + rows.map(function (row) {
        return row.map(function (value) {
          var text = String(value === null || value === undefined ? "" : value);
          return '"' + (/^[=+@-]/.test(text) ? "'" : "") + text.replace(/"/g, '""') + '"';
        }).join(";");
      }).join("\r\n");
      var link = document.createElement("a");
      var url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
      link.href = url;
      link.download = "FR-audit-" + (form.elements["agency"].value || "ALL") + "-" + $("#fr-from").value + "-" + $("#fr-to").value + ".csv";
      link.click();
      URL.revokeObjectURL(url);
    });
  }

  document.addEventListener("DOMContentLoaded", function () {
    initShell();
    initSessionDialog();
    initJournal();
    initEntry();
    initClosure();
    initRegularization();
    initReports();
  });
})();
