define(['baseView', 'loading', 'emby-input', 'emby-button', 'emby-checkbox', 'emby-scroller', 'emby-select'], function (BaseView, loading) {
    'use strict';

    var PluginUniqueId = '8a7c1f5e-3b2d-4f6a-9c1e-5d4b7a2e6f30';

    function esc(t) {
        return String(t).replace(/[&<>"]/g, function (ch) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[ch];
        });
    }

    function toast(text) {
        require(['toast'], function (t) { t(text); });
    }

    function View(view, params) {
        BaseView.apply(this, arguments);

        var self = this;
        this.list = view.querySelector('#epCatList');

        view.querySelector('form').addEventListener('submit', this.onSubmit.bind(this));
        view.querySelector('#btnCatAll').addEventListener('click', function () { self.setAll(true); });
        view.querySelector('#btnCatNone').addEventListener('click', function () { self.setAll(false); });
        view.querySelector('#txtCatFilter').addEventListener('input', function () {
            var q = this.value.trim().toLowerCase();
            Array.prototype.forEach.call(self.list.querySelectorAll('.epCatRow'), function (row) {
                row.style.display = !q || row.getAttribute('data-name').indexOf(q) >= 0 ? 'flex' : 'none';
            });
        });
    }

    Object.assign(View.prototype, BaseView.prototype);

    View.prototype.setAll = function (on) {
        Array.prototype.forEach.call(this.list.querySelectorAll('.epCatRow'), function (row) {
            if (row.style.display !== 'none') row.querySelector('.epCatOn').checked = on;
        });
    };

    View.prototype.loadCategories = function (cfg) {
        var list = this.list;
        list.innerHTML = 'Loading categories...';
        var saved = {};
        (cfg.CategorySettings || []).forEach(function (s) { saved[s.Slug] = s; });

        ApiClient.getJSON(ApiClient.getUrl('Eporner/Categories')).then(function (cats) {
            if (!cats.length) {
                list.innerHTML = 'Could not load the category list from eporner.com.';
                return;
            }
            list.innerHTML = cats.map(function (cat) {
                var s = saved[cat.Slug];
                var enabled = s ? s.Enabled : false;
                return '<div class="epCatRow" data-slug="' + esc(cat.Slug) + '" data-name="' + esc(cat.Name.toLowerCase()) + '" style="display:flex;align-items:center;gap:.8em;padding:.3em 0;">'
                    + '<input type="checkbox" class="epCatOn"' + (enabled ? ' checked' : '') + ' />'
                    + '<img src="' + esc(cat.ImageUrl) + '" style="width:64px;height:40px;object-fit:cover;border-radius:3px;" loading="lazy" />'
                    + '<span style="flex:1;">' + esc(cat.Name) + '</span>'
                    + '<input type="number" class="epCatCount" min="1" max="1000" placeholder="default" style="width:6em;" value="' + (s && s.Count ? s.Count : '') + '" />'
                    + '</div>';
            }).join('');
        }, function () {
            list.innerHTML = 'Could not load categories (is the plugin loaded?).';
        });
    };

    View.prototype.collectCategories = function () {
        return Array.prototype.map.call(this.list.querySelectorAll('.epCatRow'), function (row) {
            return {
                Slug: row.getAttribute('data-slug'),
                Enabled: row.querySelector('.epCatOn').checked,
                Count: parseInt(row.querySelector('.epCatCount').value, 10) || 0
            };
        });
    };

    View.prototype.load = function () {
        var view = this.view;
        var instance = this;
        loading.show();
        ApiClient.getPluginConfiguration(PluginUniqueId).then(function (c) {
            view.querySelector('#ddlOrder').value = c.DefaultOrder;
            view.querySelector('#ddlThumb').value = c.ThumbSize;
            view.querySelector('#ddlGay').value = c.GayMode;
            view.querySelector('#ddlTrans').value = c.TransMode;
            view.querySelector('#ddlLq').value = c.LowQualityMode;
            var maxHeight = view.querySelector('#txtMaxHeight');
            maxHeight.value = c.MaxStreamHeight;
            if (maxHeight.tagName === 'SELECT' && maxHeight.value !== String(c.MaxStreamHeight)) {
                // value saved by an older version that is not in the list: keep it selectable
                var opt = document.createElement('option');
                opt.value = c.MaxStreamHeight;
                opt.textContent = c.MaxStreamHeight + 'p';
                maxHeight.appendChild(opt);
                maxHeight.value = c.MaxStreamHeight;
            }
            view.querySelector('#txtCustomQueries').value = c.CustomQueries || '';
            view.querySelector('#txtCache').value = c.CacheMinutes;
            view.querySelector('#txtInterval').value = c.MinRequestIntervalMs;
            view.querySelector('#chkRemoved').checked = c.FilterRemovedVideos;
            view.querySelector('#chkDedupe').checked = c.DeduplicateAcrossFolders;
            view.querySelector('#chkEmbed').checked = c.AllowEmbedFallback;
            view.querySelector('#txtCatVideos').value = c.VideosPerCategory;
            instance.loadCategories(c);
            loading.hide();
        });
    };

    View.prototype.onSubmit = function (e) {
        e.preventDefault();
        loading.show();
        var view = this.view;
        var instance = this;

        ApiClient.getPluginConfiguration(PluginUniqueId).then(function (c) {
            c.DefaultOrder = view.querySelector('#ddlOrder').value;
            c.ThumbSize = view.querySelector('#ddlThumb').value;
            c.GayMode = parseInt(view.querySelector('#ddlGay').value, 10);
            c.TransMode = parseInt(view.querySelector('#ddlTrans').value, 10);
            c.LowQualityMode = parseInt(view.querySelector('#ddlLq').value, 10);
            c.MaxStreamHeight = parseInt(view.querySelector('#txtMaxHeight').value, 10) || 0;
            c.CustomQueries = view.querySelector('#txtCustomQueries').value;
            c.CacheMinutes = parseInt(view.querySelector('#txtCache').value, 10) || 0;
            c.MinRequestIntervalMs = parseInt(view.querySelector('#txtInterval').value, 10) || 0;
            c.FilterRemovedVideos = view.querySelector('#chkRemoved').checked;
            c.DeduplicateAcrossFolders = view.querySelector('#chkDedupe').checked;
            c.AllowEmbedFallback = view.querySelector('#chkEmbed').checked;
            c.VideosPerCategory = parseInt(view.querySelector('#txtCatVideos').value, 10) || 96;
            c.CategorySettings = instance.collectCategories();

            ApiClient.updatePluginConfiguration(PluginUniqueId, c).then(function () {
                loading.hide();
                toast('Saved. Now run Scheduled Tasks → Refresh Internet Channels.');
            }, function () {
                loading.hide();
                toast('Saving failed');
            });
        });
    };

    View.prototype.onResume = function (options) {
        BaseView.prototype.onResume.apply(this, arguments);
        this.load();
    };

    return View;
});
