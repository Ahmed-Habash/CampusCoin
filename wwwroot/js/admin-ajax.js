(() => {
    const ensureToastStyles = () => {
        if (document.getElementById('admin-ajax-toast-style')) return;
        const style = document.createElement('style');
        style.id = 'admin-ajax-toast-style';
        style.textContent = `
.admin-ajax-toast{
  position:fixed;top:18px;right:18px;z-index:200000;max-width:min(420px,calc(100vw - 28px));
  padding:12px 14px;border-radius:12px;border:1px solid var(--line,#e2e8f0);
  background:var(--panel,#fff);color:var(--ink,#0f172a);
  box-shadow:0 16px 40px rgba(15,23,42,.18);font:650 13px/1.4 inherit;
  animation:admin-toast-in .22s ease both;
}
.admin-ajax-toast.is-error{border-color:rgba(186,75,67,.35);background:#fff5f4;color:#9f2d24}
.admin-ajax-toast.is-ok{border-color:rgba(20,131,97,.28);background:#f0fdf7;color:#0f6b4c}
.admin-ajax-toast.is-hiding{opacity:0;transform:translateY(-6px);transition:.22s ease}
@keyframes admin-toast-in{from{opacity:0;transform:translateY(-8px)}to{opacity:1;transform:none}}
.admin-confirm-overlay{
  position:fixed;inset:0;z-index:200001;display:grid;place-items:center;padding:20px;
  background:rgba(8,17,31,.55);backdrop-filter:blur(8px);
}
.admin-confirm-overlay[hidden]{display:none!important}
.admin-confirm-card{
  width:min(92vw,400px);background:var(--panel,#fff);border:1px solid var(--line,#e2e8f0);
  border-radius:18px;padding:22px 22px 18px;box-shadow:0 28px 70px rgba(0,0,0,.28);
}
.admin-confirm-card h3{margin:0 0 8px;font-size:1.05rem;letter-spacing:-.03em;font-weight:800}
.admin-confirm-card p{margin:0 0 18px;font-size:13px;line-height:1.55;color:var(--muted,#64748b)}
.admin-confirm-actions{display:flex;justify-content:flex-end;gap:10px;flex-wrap:wrap}
.admin-user-status{
  display:inline-flex;align-items:center;gap:6px;min-height:24px;padding:3px 10px;border-radius:999px;
  font-size:11px;font-weight:800;letter-spacing:.02em;
}
.admin-user-status.is-active{background:rgba(20,131,97,.12);color:#148361}
.admin-user-status.is-disabled{background:rgba(186,75,67,.12);color:#ba4b43}
.admin-action-btn{
  min-height:32px!important;padding:6px 12px!important;font-size:11px!important;font-weight:750!important;
  border-radius:10px!important;
}
.admin-action-btn.is-danger{
  border-color:rgba(186,75,67,.45)!important;color:#ba4b43!important;background:rgba(186,75,67,.06)!important;
}
.admin-action-btn.is-danger:hover{background:rgba(186,75,67,.12)!important}
.admin-action-btn.is-safe{
  border-color:rgba(20,131,97,.35)!important;color:#148361!important;background:rgba(20,131,97,.06)!important;
}
.admin-action-btn.is-busy{opacity:.65;cursor:wait}
`;
        document.head.append(style);
    };

    const toast = (message, kind = 'success') => {
        ensureToastStyles();
        const el = document.createElement('div');
        el.className = `admin-ajax-toast ${kind === 'error' ? 'is-error' : 'is-ok'}`;
        el.setAttribute('role', kind === 'error' ? 'alert' : 'status');
        el.textContent = message;
        document.body.append(el);
        setTimeout(() => {
            el.classList.add('is-hiding');
            setTimeout(() => el.remove(), 220);
        }, 3400);
    };

    const confirmAsync = (title, message, confirmLabel = 'Confirm') => new Promise(resolve => {
        ensureToastStyles();
        document.querySelectorAll('.admin-confirm-overlay').forEach(el => el.remove());
        const overlay = document.createElement('div');
        overlay.className = 'admin-confirm-overlay';
        overlay.innerHTML = `
          <div class="admin-confirm-card" role="dialog" aria-modal="true">
            <h3></h3>
            <p></p>
            <div class="admin-confirm-actions">
              <button type="button" class="button secondary small" data-cancel>Cancel</button>
              <button type="button" class="button small admin-action-btn is-danger" data-ok></button>
            </div>
          </div>`;
        overlay.querySelector('h3').textContent = title;
        overlay.querySelector('p').textContent = message;
        overlay.querySelector('[data-ok]').textContent = confirmLabel;
        const finish = (value) => {
            overlay.remove();
            resolve(value);
        };
        overlay.querySelector('[data-cancel]').addEventListener('click', () => finish(false));
        overlay.querySelector('[data-ok]').addEventListener('click', () => finish(true));
        overlay.addEventListener('click', (e) => { if (e.target === overlay) finish(false); });
        document.addEventListener('keydown', function onKey(e) {
            if (e.key === 'Escape') {
                document.removeEventListener('keydown', onKey);
                finish(false);
            }
        });
        document.body.append(overlay);
        overlay.querySelector('[data-ok]').focus();
    });

    const tokenFrom = (form) =>
        form.querySelector('input[name="__RequestVerificationToken"]')?.value
        || document.querySelector('input[name="__RequestVerificationToken"]')?.value
        || '';

    const postForm = async (form) => {
        const action = form.getAttribute('action');
        if (!action) throw new Error('Missing form action.');
        const token = tokenFrom(form);
        const body = new FormData(form);
        if (token && !body.get('__RequestVerificationToken')) {
            body.set('__RequestVerificationToken', token);
        }

        const response = await fetch(action, {
            method: 'POST',
            body,
            headers: {
                'X-Requested-With': 'XMLHttpRequest',
                'Accept': 'application/json',
                ...(token ? { 'RequestVerificationToken': token } : {})
            },
            credentials: 'same-origin',
            redirect: 'follow'
        });

        const raw = await response.text();
        let data = null;
        try { data = raw ? JSON.parse(raw) : null; } catch { data = null; }

        if (data && typeof data === 'object') {
            if (!response.ok || data.ok === false) {
                throw new Error(data.message || data.error || `Request failed (${response.status}).`);
            }
            if (data.ok === true || data.message || data.data) {
                return { ok: true, message: data.message || 'Saved.', data: data.data ?? data };
            }
        }

        // Server returned HTML (classic redirect). Action likely succeeded — reload.
        if (response.ok || response.redirected) {
            return { ok: true, message: 'Saved.', data: null, reload: true };
        }

        throw new Error(`Request failed (${response.status}). Refresh and try again.`);
    };

    const setBusy = (form, on) => {
        form.querySelectorAll('button').forEach(btn => {
            btn.disabled = on;
            btn.classList.toggle('is-busy', on);
        });
    };

    const closestItem = (form) =>
        form.closest('[data-ajax-item]') || form.closest('[data-user-row]') || form.closest('tr');

    const nativeSubmit = (form) => {
        form.removeAttribute('data-admin-ajax');
        HTMLFormElement.prototype.submit.call(form);
    };

    const renderUserActions = (row, data, token) => {
        const actions = row.querySelector('[data-user-actions]');
        const statusEl = row.querySelector('[data-user-status]');
        if (!actions || !statusEl) return;
        const userId = row.dataset.userId;
        const isPrimary = row.dataset.primary === 'true';
        const canDisable = document.body.dataset.canDisable === 'true';
        const canManagePerms = document.body.dataset.canManagePerms === 'true';
        const role = data.role || row.dataset.role || 'Student';
        const disabled = !!data.disabled;

        statusEl.className = `admin-user-status ${disabled ? 'is-disabled' : 'is-active'}`;
        statusEl.textContent = disabled ? 'Disabled' : 'Active';
        row.dataset.role = role;

        let html = '';
        if (canDisable) {
            if (disabled) {
                html += `<form method="post" action="/admin/users/${userId}/enable" data-admin-ajax data-ajax-silent style="display:inline;">
                    <input type="hidden" name="__RequestVerificationToken" value="${token}" />
                    <button type="submit" class="button small secondary admin-action-btn is-safe">Enable</button>
                </form>`;
            } else if (!isPrimary) {
                html += `<form method="post" action="/admin/users/${userId}/disable" data-admin-ajax data-ajax-silent data-confirm-title="Disable account?" data-confirm-message="This user will not be able to sign in until you enable the account again." data-confirm-label="Disable account" style="display:inline;">
                    <input type="hidden" name="__RequestVerificationToken" value="${token}" />
                    <button type="submit" class="button small secondary admin-action-btn is-danger">Disable</button>
                </form>`;
            }
        }
        if (role === 'Administrator' && !isPrimary && canManagePerms) {
            html += `<form method="post" action="/admin/users/${userId}/remove-admin" data-admin-ajax data-ajax-reload data-ajax-silent data-confirm-title="Revoke administrator?" data-confirm-message="This account will move back to the student list." data-confirm-label="Revoke admin" style="display:inline;">
                <input type="hidden" name="__RequestVerificationToken" value="${token}" />
                <button type="submit" class="button small secondary admin-action-btn">Revoke Admin</button>
            </form>`;
        }
        if (role === 'Student' && canManagePerms) {
            html += `<form method="post" action="/admin/users/${userId}/make-admin" data-admin-ajax data-ajax-reload data-ajax-silent style="display:inline;">
                <input type="hidden" name="__RequestVerificationToken" value="${token}" />
                <input type="hidden" name="canDisableUsers" value="true" />
                <input type="hidden" name="canManageCategories" value="true" />
                <input type="hidden" name="canManageAnnouncements" value="true" />
                <input type="hidden" name="canManageTipTemplates" value="true" />
                <button type="submit" class="button small admin-action-btn">Grant Admin</button>
            </form>`;
        }
        actions.innerHTML = html;
    };

    document.addEventListener('submit', async (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) return;
        if (!form.matches('[data-admin-ajax]')) return;
        event.preventDefault();

        const title = form.dataset.confirmTitle || (form.dataset.confirm ? 'Please confirm' : '');
        const message = form.dataset.confirmMessage || form.dataset.confirm || '';
        const label = form.dataset.confirmLabel || 'Confirm';
        if (message) {
            const ok = await confirmAsync(title || 'Please confirm', message, label);
            if (!ok) return;
        }

        setBusy(form, true);
        try {
            const result = await postForm(form);
            const role = result?.data?.role;
            const shouldReload = result.reload
                || form.hasAttribute('data-ajax-reload')
                || role === 'Administrator'
                || (role === 'Student' && /remove-admin/i.test(form.getAttribute('action') || ''));

            if (shouldReload) {
                if (/hard-delete|\/delete$/i.test(form.getAttribute('action') || '') && /groups/i.test(form.getAttribute('action') || '')) {
                    window.location.href = '/admin/groups';
                    return;
                }
                window.location.reload();
                return;
            }

            if (!form.hasAttribute('data-ajax-silent') && result.message) {
                toast(result.message);
            }
            form.dispatchEvent(new CustomEvent('admin:ajax-success', {
                bubbles: true,
                detail: result
            }));

            if (form.hasAttribute('data-ajax-remove')) {
                const item = closestItem(form);
                if (item) {
                    item.style.transition = 'opacity .2s ease, transform .2s ease';
                    item.style.opacity = '0';
                    item.style.transform = 'translateY(-4px)';
                    setTimeout(() => item.remove(), 200);
                }
            }
        } catch (error) {
            const msg = error?.message || 'Request failed.';
            // Network / parse failures: fall back to a normal post so the action still completes.
            if (/failed to fetch|network|unexpected/i.test(msg)) {
                toast('Retrying with a full page submit…', 'error');
                nativeSubmit(form);
                return;
            }
            toast(msg, 'error');
            form.dispatchEvent(new CustomEvent('admin:ajax-error', {
                bubbles: true,
                detail: { message: msg }
            }));
        } finally {
            setBusy(form, false);
        }
    });

    document.addEventListener('admin:ajax-success', (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) return;
        const detail = event.detail || {};
        const data = detail.data || {};
        const row = form.closest('[data-user-row]');
        if (!row) return;

        // Role changes must refresh lists (admin ↔ student tables).
        if (data.role && data.role !== row.dataset.role) {
            location.reload();
            return;
        }

        const token = tokenFrom(form);
        if (typeof data.disabled === 'boolean') {
            renderUserActions(row, data, token);
        }
    });

    ensureToastStyles();
    document.querySelectorAll('[data-user-status]').forEach(el => {
        const disabled = /disabled/i.test(el.textContent || '');
        el.className = `admin-user-status ${disabled ? 'is-disabled' : 'is-active'}`;
    });
    document.querySelectorAll('[data-user-actions] form[action*="/disable"] button').forEach(btn => {
        btn.classList.add('admin-action-btn', 'is-danger');
    });
    document.querySelectorAll('[data-user-actions] form[action*="/enable"] button').forEach(btn => {
        btn.classList.add('admin-action-btn', 'is-safe');
    });

    window.CampusAdminAjax = { toast, postForm, confirmAsync };
})();
