// @ts-check
/*
 * WebMCP page tools.
 *
 * Tools that work on the page the user sees: what the page is, where its menus lead, opening another page of the same
 * site, and reading and filling its forms. They never submit a form and never leave the site, so the user always
 * reviews and saves the changes an agent makes.
 *
 * The tools are registered with the standard `document.modelContext.registerTool`, so any WebMCP agent can use them:
 * the browser's own assistant, or the CrestApps AI chat. As WebMCP defines, each answers a JSON-serializable value and
 * reports a failure by rejecting. Load webmcp-polyfill.js first in browsers without native WebMCP.
 */

(function () {
  'use strict';

  var MAX_HEADINGS = 20;
  var MAX_LINKS = 200;
  var MAX_FIELDS = 200;
  var MAX_VALUE_LENGTH = 2000;

  // The selectors the tools read the page with.
  var selectors = {
    description: 'meta[name="description"]',
    headings: 'h1, h2',
    links: 'nav a[href], header a[href], aside a[href], [role="navigation"] a[href]',
    fields: 'form input, form select, form textarea',
  };

  // Field types an agent never reads or fills.
  var SKIPPED_TYPES = ['hidden', 'password', 'file', 'submit', 'button', 'reset', 'image'];
  var SKIPPED_NAMES = ['__RequestVerificationToken'];

  function cleanText(value) {
    return (value == null ? '' : String(value)).replace(/\s+/g, ' ').trim();
  }


  function resolveSameOrigin(href, loc) {
    if (!href) {
      return null;
    }

    try {
      var url = new URL(href, loc.href);

      return url.origin === loc.origin ? url : null;
    } catch (error) {
      return null;
    }
  }

  function fieldType(field) {
    var tag = (field.tagName || '').toLowerCase();

    if (tag === 'select' || tag === 'textarea') {
      return tag;
    }

    return (field.type || 'text').toLowerCase();
  }

  function fieldLabel(field) {
    if (field.labels && field.labels.length > 0) {
      var label = cleanText(field.labels[0].textContent);

      if (label) {
        return label;
      }
    }

    return cleanText(field.getAttribute('aria-label')) || cleanText(field.getAttribute('placeholder'));
  }

  function isListed(field) {
    return !field.disabled &&
      SKIPPED_TYPES.indexOf(fieldType(field)) < 0 &&
      SKIPPED_NAMES.indexOf(field.name) < 0;
  }

  function listFields(doc) {
    var fields = [];
    var all = doc.querySelectorAll(selectors.fields);

    for (var i = 0; i < all.length && fields.length < MAX_FIELDS; i++) {
      if (isListed(all[i])) {
        fields.push(all[i]);
      }
    }

    return fields;
  }

  function describeField(field) {
    var type = fieldType(field);
    var description = {
      key: field.name || field.id,
      name: field.name,
      id: field.id,
      label: fieldLabel(field),
      type: type,
      value: type === 'checkbox' || type === 'radio'
        ? !!field.checked
        : String(field.value == null ? '' : field.value).substring(0, MAX_VALUE_LENGTH),
      required: !!field.required,
    };

    if (type === 'select') {
      description.options = Array.prototype.map.call(field.options || [], function (option) {
        return { value: option.value, text: cleanText(option.text), selected: !!option.selected };
      });
    }

    return description;
  }

  function findField(doc, key) {
    var wanted = cleanText(key).toLowerCase();

    if (!wanted) {
      return null;
    }

    var fields = listFields(doc);
    var matchers = [
      function (field) {
        return (field.name || '').toLowerCase() === wanted;
      },
      function (field) {
        return (field.id || '').toLowerCase() === wanted;
      },
      function (field) {
        return fieldLabel(field).toLowerCase() === wanted;
      },
    ];

    for (var m = 0; m < matchers.length; m++) {
      for (var i = 0; i < fields.length; i++) {
        if (matchers[m](fields[i])) {
          return fields[i];
        }
      }
    }

    return null;
  }

  function raise(field, type) {
    field.dispatchEvent(new Event(type, { bubbles: true }));
  }

  /**
   * Creates the page tools for a document.
   *
   * @param {{ doc: any, loc: any, isAdmin?: boolean, navigateDelayMs?: number }} options The document, its location,
   *   whether it is an admin page, and how long page_navigate waits after answering before it opens the page.
   */
  function createPageTools(options) {
    var doc = options.doc;
    var loc = options.loc;
    var navigateDelayMs = typeof options.navigateDelayMs === 'number' ? options.navigateDelayMs : 400;

    return [
      {
        name: 'page_get_info',
        title: 'Get page info',
        description: 'Describes the page the user has open: its title, address, description and main headings, and whether it is an admin page.',
        inputSchema: { type: 'object', properties: {} },
        annotations: { readOnlyHint: true },
        execute: function () {
          var meta = doc.querySelector(selectors.description);
          var headings = [];
          var all = doc.querySelectorAll(selectors.headings);

          for (var i = 0; i < all.length && headings.length < MAX_HEADINGS; i++) {
            var text = cleanText(all[i].textContent);

            if (text) {
              headings.push({ level: (all[i].tagName || '').toUpperCase() === 'H1' ? 1 : 2, text: text });
            }
          }

          return ({
            title: doc.title,
            url: loc.href,
            description: meta ? meta.getAttribute('content') : null,
            headings: headings,
            isAdmin: !!options.isAdmin,
          });
        },
      },
      {
        name: 'page_list_links',
        title: 'List page links',
        description: 'Lists the links in the page\'s menus and navigation that stay on this site, with their text. Use it to find the page to open for a task.',
        inputSchema: {
          type: 'object',
          properties: {
            filter: { type: 'string', description: 'Only list links whose text contains these words.' },
          },
        },
        annotations: { readOnlyHint: true },
        execute: function (input) {
          var filter = cleanText(input && input.filter).toLowerCase();
          var seen = new Set();
          var links = [];
          var all = doc.querySelectorAll(selectors.links);

          for (var i = 0; i < all.length && links.length < MAX_LINKS; i++) {
            var url = resolveSameOrigin(all[i].getAttribute('href'), loc);
            var text = cleanText(all[i].textContent);

            if (!url || !text || seen.has(url.href) || (filter && text.toLowerCase().indexOf(filter) < 0)) {
              continue;
            }

            seen.add(url.href);
            links.push({ text: text, url: url.href });
          }

          return ({ links: links });
        },
      },
      {
        name: 'page_navigate',
        title: 'Open page',
        description: 'Opens another page of this site in the current tab. Only addresses on this site are allowed; take them from page_list_links, and start a site path with /. Unsaved changes on the current page are lost.',
        inputSchema: {
          type: 'object',
          properties: {
            url: { type: 'string', description: 'The address to open, relative to the site or absolute on the same site.' },
          },
          required: ['url'],
        },
        execute: function (input) {
          var url = resolveSameOrigin(input && input.url, loc);

          if (!url) {
            throw new Error('Only addresses on this site can be opened.');
          }

          // Answer first and open the page a moment later, so the agent gets the result before this page (and
          // the chat connection on it) goes away. The chat finishes the reply on the server and shows it on the
          // next page.
          setTimeout(function () {
            loc.assign(url.href);
          }, navigateDelayMs);

          return ({ navigatedTo: url.href });
        },
      },
      {
        name: 'page_list_form_fields',
        title: 'List form fields',
        description: 'Lists the fields of the forms on the page: name, label, type, current value, whether it is required and, for lists, the choices. Hidden, password and file fields are not listed.',
        inputSchema: { type: 'object', properties: {} },
        annotations: { readOnlyHint: true },
        execute: function () {
          return ({ fields: listFields(doc).map(describeField) });
        },
      },
      {
        name: 'page_set_form_field',
        title: 'Set form field',
        description: 'Puts a value into one form field on the page, found by its name, id or label. It does not save or submit the form: the user reviews the change and saves it.',
        inputSchema: {
          type: 'object',
          properties: {
            field: { type: 'string', description: 'The field\'s name, id or label, as page_list_form_fields lists it.' },
            value: { description: 'The value. For a check box, true or false. For a list, a choice\'s value or text.' },
          },
          required: ['field', 'value'],
        },
        execute: function (input) {
          var field = findField(doc, input && input.field);

          if (!field) {
            throw new Error('No editable field on the page is named "' + cleanText(input && input.field) + '".');
          }

          var type = fieldType(field);
          var value = input.value;

          if (type === 'checkbox' || type === 'radio') {
            field.checked = value === true || String(value).toLowerCase() === 'true';
          } else if (type === 'select') {
            var wanted = cleanText(value).toLowerCase();
            var option = Array.prototype.find.call(field.options || [], function (candidate) {
              return String(candidate.value).toLowerCase() === wanted || cleanText(candidate.text).toLowerCase() === wanted;
            });

            if (!option) {
              throw new Error('The list "' + fieldLabel(field) + '" has no choice "' + cleanText(value) + '".');
            }

            field.value = option.value;
          } else {
            field.value = value == null ? '' : String(value);
          }

          raise(field, 'input');
          raise(field, 'change');

          return ({ field: describeField(field) });
        },
      },
    ];
  }

  /**
   * Registers the page tools with the page's ModelContext.
   *
   * @param {any} modelContext The page's ModelContext (document.modelContext).
   * @param {{ doc: any, loc: any, isAdmin?: boolean }} options The document, its location, and whether it is an admin page.
   * @param {AbortSignal} [signal] Unregisters the tools when aborted.
   */
  function register(modelContext, options, signal) {
    return Promise.all(createPageTools(options).map(function (tool) {
      return modelContext.registerTool(tool, signal ? { signal: signal } : {}).catch(function (error) {
        console.warn('The page tool "' + tool.name + '" could not be registered.', error);
      });
    }));
  }

  window.CrestAppsWebMcpPageTools = {
    selectors: selectors,
    createPageTools: createPageTools,
    register: register,
  };

  if (window.document && window.document.modelContext) {
    var config = window.crestAppsWebMcp && window.crestAppsWebMcp.config;

    register(window.document.modelContext, {
      doc: window.document,
      loc: window.location,
      isAdmin: !!(config && config.isAdmin),
    });
  }
})();
