// Runs at document start in every frame of the recorded browser (Playwright init script).
// Each user action is reported with a DOM snapshot taken in the capture phase, before the
// page reacts, then a second snapshot once the page has had time to settle.
(() => {
  const report = window.__sessionRecorderReport;
  if (!report || window.__sessionRecorderActive) return;
  window.__sessionRecorderActive = true;

  const SETTLE_DELAY_MS = 1000;
  const TEXT_PREVIEW_LENGTH = 120;
  const MASKED_VALUE = '********';
  const RECORDED_KEYS = new Set(['Enter', 'Tab', 'Escape']);
  const STABLE_ATTRIBUTES = ['data-testid', 'data-test', 'data-qa', 'data-cy', 'name', 'aria-label', 'placeholder'];
  const GENERATED_ID = /\d{4,}|[0-9a-f]{10,}|^:/i;
  // Keep in sync with SecretName in Services/Recording/SecretRedactor.cs.
  const SECRET_NAME = /password|passwd|secret|token|api[-_]?key|credential|session[-_]?id|^(pwd|pass|otp|sysparm_ck)$/i;
  const VOID_ELEMENTS = new Set(['area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'source', 'track', 'wbr']);
  const RAW_TEXT_ELEMENTS = new Set(['script', 'style']);
  const URL_ATTRIBUTES = ['href', 'src', 'action'];
  const isTopFrame = window === window.top;

  window.addEventListener('click', onClick, true);
  window.addEventListener('change', onChange, true);
  window.addEventListener('focusout', onFocusOut, true);
  window.addEventListener('keydown', onKeyDown, true);
  window.addEventListener('submit', onSubmit, true);
  // Every frame, not only the top one: apps like ServiceNow load their pages inside a frame.
  // about:blank and srcdoc frames carry no page of their own.
  if (/^(https?|file):$/.test(location.protocol)) {
    window.addEventListener('load', () => reportStep({ kind: 'page-load' }, null), { once: true });
  }

  function onClick(event) {
    const target = actionTarget(event);
    if (target) reportAction('click', target);
  }

  function onChange(event) {
    const target = actionTarget(event);
    if (target) reportAction('change', target, { value: fieldValue(target) });
  }

  // Rich-text editors never fire "change"; their committed text is recorded when they lose focus.
  function onFocusOut(event) {
    const target = actionTarget(event);
    if (target?.isContentEditable) reportAction('edit', target, { value: target.innerText });
  }

  function onKeyDown(event) {
    if (!RECORDED_KEYS.has(event.key)) return;
    const target = actionTarget(event);
    if (target) reportAction('key', target, { key: event.key });
  }

  function onSubmit(event) {
    const target = actionTarget(event);
    if (target) reportAction('submit', target);
  }

  function reportAction(kind, target, details = {}) {
    reportStep({ kind, target: describeElement(target), ...details }, serializeDocument());
  }

  function reportStep(step, domBefore) {
    const stepKey = crypto.randomUUID();
    report(JSON.stringify({
      type: 'step',
      stepKey,
      domBefore,
      step: { ...step, url: location.href, title: document.title, inIframe: !isTopFrame, at: Date.now() },
    }));
    setTimeout(() => report(JSON.stringify({ type: 'settled', stepKey, domAfter: serializeDocument() })), SETTLE_DELAY_MS);
  }

  function actionTarget(event) {
    return event.composedPath().find(node => node instanceof Element) ?? null;
  }

  function fieldValue(field) {
    if (field instanceof HTMLSelectElement) {
      return [...field.selectedOptions].map(option => ({ value: option.value, label: option.label }));
    }
    if (field.type === 'checkbox' || field.type === 'radio') return field.checked;
    if (field.type === 'file') return [...field.files].map(file => file.name);
    if (isSecretField(field)) return MASKED_VALUE;
    return field.value;
  }

  // Password inputs, and fields whose name says they hold a secret (CSRF tokens in hidden inputs).
  function isSecretField(field) {
    return field.type === 'password' || SECRET_NAME.test(field.name ?? '') || SECRET_NAME.test(field.id ?? '');
  }

  // Hand-written serialiser instead of outerHTML: outerHTML drops open shadow roots and
  // typed form values. Shadow roots are written as declarative <template shadowrootmode>.
  function serializeDocument() {
    const doctype = document.doctype ? `<!DOCTYPE ${document.doctype.name}>\n` : '';
    return doctype + serializeElement(document.documentElement);
  }

  function serializeElement(element) {
    const tag = element.localName;
    const attributes = serializeAttributes(element);
    if (VOID_ELEMENTS.has(tag)) return `<${tag}${attributes}>`;
    const shadow = element.shadowRoot
      ? `<template shadowrootmode="${element.shadowRoot.mode}">${serializeChildren(element.shadowRoot)}</template>`
      : '';
    return `<${tag}${attributes}>${shadow}${serializeContent(element)}</${tag}>`;
  }

  function serializeContent(element) {
    if (element instanceof HTMLTemplateElement) return serializeChildren(element.content);
    if (element instanceof HTMLTextAreaElement) return escapeText(element.value);
    return serializeChildren(element);
  }

  function serializeChildren(parent) {
    let html = '';
    for (const child of parent.childNodes) html += serializeNode(child);
    return html;
  }

  function serializeNode(node) {
    switch (node.nodeType) {
      case Node.ELEMENT_NODE: return serializeElement(node);
      case Node.TEXT_NODE: return RAW_TEXT_ELEMENTS.has(node.parentNode?.localName) ? node.data : escapeText(node.data);
      case Node.COMMENT_NODE: return `<!--${node.data}-->`;
      default: return '';
    }
  }

  function serializeAttributes(element) {
    const attributes = new Map([...element.attributes].map(attribute => [attribute.name, attribute.value]));
    if (element instanceof HTMLOptionElement) setFlag(attributes, 'selected', element.selected);
    if (element instanceof HTMLInputElement) applyInputState(element, attributes);
    for (const name of URL_ATTRIBUTES) {
      if (attributes.has(name)) attributes.set(name, redactUrl(attributes.get(name)));
    }
    let html = '';
    for (const [name, attributeValue] of attributes) html += ` ${name}="${escapeAttribute(attributeValue)}"`;
    return html;
  }

  function applyInputState(input, attributes) {
    if (input.type === 'checkbox' || input.type === 'radio') setFlag(attributes, 'checked', input.checked);
    else if (input.type === 'file') attributes.set('data-recorded-files', [...input.files].map(file => file.name).join(', '));
    else attributes.set('value', isSecretField(input) ? MASKED_VALUE : input.value);
  }

  // Same rule as SecretRedactor.RedactUrl: secret-named query parameters keep their name, lose their value.
  function redactUrl(url) {
    const queryStart = url.indexOf('?');
    if (queryStart < 0) return url;
    const pairs = url.slice(queryStart + 1).split('&').map(pair => {
      const separator = pair.indexOf('=');
      return separator >= 0 && SECRET_NAME.test(pair.slice(0, separator)) ? pair.slice(0, separator + 1) + MASKED_VALUE : pair;
    });
    return url.slice(0, queryStart + 1) + pairs.join('&');
  }

  function setFlag(attributes, name, isSet) {
    if (isSet) attributes.set(name, '');
    else attributes.delete(name);
  }

  function escapeText(text) {
    return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  function escapeAttribute(text) {
    return text.replace(/&/g, '&amp;').replace(/"/g, '&quot;');
  }

  function describeElement(element) {
    return {
      selector: cssSelector(element),
      xpath: xPath(element),
      tag: element.localName,
      id: element.id || null,
      name: element.getAttribute('name'),
      type: element.getAttribute('type'),
      role: element.getAttribute('role'),
      label: accessibleLabel(element),
      text: textPreview(element.innerText),
      inShadowDom: element.getRootNode() instanceof ShadowRoot,
    };
  }

  function accessibleLabel(element) {
    const labelledBy = element.getAttribute('aria-labelledby');
    const labelledByText = labelledBy && document.getElementById(labelledBy)?.innerText;
    return textPreview(
      element.getAttribute('aria-label') ||
      labelledByText ||
      element.labels?.[0]?.innerText ||
      element.getAttribute('placeholder') ||
      element.getAttribute('title'),
    );
  }

  function textPreview(text) {
    const compact = (text ?? '').replace(/\s+/g, ' ').trim();
    return compact ? compact.slice(0, TEXT_PREVIEW_LENGTH) : null;
  }

  // Walks up from the element until an ancestor (or the element itself) has a unique,
  // human-meaningful attribute, so the selector survives layout changes where possible.
  function cssSelector(element) {
    const root = element.getRootNode();
    const parts = [];
    for (let node = element; node instanceof Element; node = node.parentElement) {
      const anchor = uniqueAttributeSelector(node, root);
      if (anchor) return [anchor, ...parts].join(' > ');
      parts.unshift(`${node.localName}:nth-of-type(${sameTagIndex(node)})`);
    }
    return parts.join(' > ');
  }

  function uniqueAttributeSelector(node, root) {
    const candidates = [];
    if (node.id && !GENERATED_ID.test(node.id)) candidates.push(`#${CSS.escape(node.id)}`);
    for (const attribute of STABLE_ATTRIBUTES) {
      const attributeValue = node.getAttribute(attribute);
      if (attributeValue) candidates.push(`${node.localName}[${attribute}="${CSS.escape(attributeValue)}"]`);
    }
    return candidates.find(selector => root.querySelectorAll(selector).length === 1) ?? null;
  }

  function xPath(element) {
    const parts = [];
    for (let node = element; node instanceof Element; node = node.parentElement) {
      parts.unshift(`${node.localName}[${sameTagIndex(node)}]`);
    }
    return `/${parts.join('/')}`;
  }

  function sameTagIndex(node) {
    let index = 1;
    for (let sibling = node.previousElementSibling; sibling; sibling = sibling.previousElementSibling) {
      if (sibling.localName === node.localName) index++;
    }
    return index;
  }
})();
