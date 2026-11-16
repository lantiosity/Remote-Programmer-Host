'use strict';
((root) => {
  const clamp = (value, min, max) => Math.max(min, Math.min(value, max));

  function metrics(viewport, extent, offset, track = viewport, minimum = 0) {
    const range = Math.max(0, extent - viewport);
    const size = range ? Math.min(track, Math.max(minimum, track * viewport / extent)) : track;
    const travel = Math.max(0, track - size);
    return { range, size, travel, position: range ? clamp(offset, 0, range) / range * travel : 0 };
  }

  function offsetForThumb(position, geometry) {
    return geometry.travel ? clamp(position / geometry.travel, 0, 1) * geometry.range : 0;
  }

  function install(doc) {
    const entries = [], layers = new Map();
    let frame = 0;
    const delay = 2200;

    function schedule() {
      if (!frame) frame = root.requestAnimationFrame(() => {
        frame = 0;
        entries.forEach(update);
      });
    }

    function layerFor(element) {
      // Keep menu rails inside the popup for outside-click detection, and dialog
      // rails in the browser's top layer rather than behind the modal backdrop.
      const parent = element.closest('.picker-popup, dialog') || doc.body;
      if (!layers.has(parent)) {
        const layer = doc.createElement('div'); layer.className = 'scroll-overlay';
        layer.dataset.scrollOverlay = ''; parent.append(layer); layers.set(parent, layer);
      }
      return layers.get(parent);
    }

    function makeAxis(element, direction, label) {
      const vertical = direction === 'vertical';
      const rail = doc.createElement('div'); rail.className = 'scroll-rail ' + direction; rail.hidden = true;
      rail.tabIndex = 0; rail.setAttribute('role', 'scrollbar');
      rail.setAttribute('aria-controls', element.id); rail.setAttribute('aria-orientation', direction);
      rail.setAttribute('aria-label', label + (vertical ? '垂直滚动条' : '水平滚动条'));
      rail.setAttribute('aria-valuemin', '0');
      const thumb = doc.createElement('div'); thumb.className = 'scroll-thumb'; rail.append(thumb);
      layerFor(element).append(rail);
      const axis = { rail, thumb, vertical, geometry: null, start: 0, hovered: false, focused: false, dragging: false, timer: 0 };

      function widen() { root.clearTimeout(axis.timer); rail.classList.add('wide'); }
      function engage() { rail.classList.toggle('engaged', axis.hovered || axis.dragging || axis.focused); }
      function settle() {
        root.clearTimeout(axis.timer);
        if (axis.hovered || axis.dragging || axis.focused) return;
        axis.timer = root.setTimeout(() => rail.classList.remove('wide'), delay);
      }
      axis.pulse = () => { widen(); settle(); };
      const setOffset = value => { if (vertical) element.scrollTop = value; else element.scrollLeft = value; schedule(); };
      const getOffset = () => vertical ? element.scrollTop : element.scrollLeft;
      const point = event => vertical ? event.clientY : event.clientX;
      rail.addEventListener('pointerenter', () => { axis.hovered = true; engage(); widen(); });
      rail.addEventListener('pointerleave', () => { axis.hovered = false; engage(); settle(); });
      rail.addEventListener('focus', () => { axis.focused = true; engage(); widen(); });
      rail.addEventListener('blur', () => { axis.focused = false; engage(); settle(); });
      rail.addEventListener('pointerdown', event => {
        if (event.button !== 0 || !axis.geometry) return;
        event.preventDefault(); widen(); axis.dragging = true; engage();
        rail.setPointerCapture(event.pointerId);
        const local = point(event) - axis.start;
        if (local < axis.geometry.position || local > axis.geometry.position + axis.geometry.size)
          setOffset(offsetForThumb(local - axis.geometry.size / 2, axis.geometry));
        axis.dragPoint = point(event); axis.dragOffset = getOffset();
      });
      rail.addEventListener('pointermove', event => {
        if (!axis.dragging || !axis.geometry?.travel) return;
        setOffset(axis.dragOffset + (point(event) - axis.dragPoint) / axis.geometry.travel * axis.geometry.range);
      });
      function endDrag() {
        axis.dragging = false; axis.hovered = rail.matches(':hover'); engage(); settle();
      }
      rail.addEventListener('pointerup', endDrag); rail.addEventListener('pointercancel', endDrag);
      rail.addEventListener('lostpointercapture', endDrag);
      rail.addEventListener('wheel', event => {
        const textStyle = root.getComputedStyle(element);
        const line = parseFloat(textStyle.lineHeight) || parseFloat(textStyle.fontSize) * 1.5;
        const unit = event.deltaMode === 1 ? line : event.deltaMode === 2 ? (vertical ? element.clientHeight : element.clientWidth) : 1;
        const delta = vertical ? event.deltaY : event.deltaX || event.deltaY;
        if (delta) { event.preventDefault(); setOffset(getOffset() + delta * unit); axis.pulse(); }
      }, { passive: false });
      rail.addEventListener('keydown', event => {
        if (!axis.geometry) return;
        const viewport = vertical ? element.clientHeight : element.clientWidth;
        let next = getOffset();
        const step = parseFloat(root.getComputedStyle(element).fontSize) * 3;
        if (event.key === (vertical ? 'ArrowDown' : 'ArrowRight')) next += step;
        else if (event.key === (vertical ? 'ArrowUp' : 'ArrowLeft')) next -= step;
        else if (event.key === 'PageDown' || event.key === ' ') next += viewport * .9 * (event.shiftKey ? -1 : 1);
        else if (event.key === 'PageUp') next -= viewport * .9;
        else if (event.key === 'Home') next = 0;
        else if (event.key === 'End') next = axis.geometry.range;
        else return;
        event.preventDefault(); setOffset(next);
      });
      return axis;
    }

    function update(entry) {
      const { element, axes } = entry;
      const bounds = element.getBoundingClientRect();
      const visible = element.getClientRects().length && bounds.width > 0 && bounds.height > 0;
      const rect = { left: bounds.left + element.clientLeft, top: bounds.top + element.clientTop,
        width: element.clientWidth, height: element.clientHeight };
      const clip = { left: 0, top: 0, right: root.innerWidth, bottom: root.innerHeight };
      if (visible) for (let parent = element.parentElement; parent && parent !== doc.body; parent = parent.parentElement) {
        const style = root.getComputedStyle(parent), box = parent.getBoundingClientRect();
        if (/auto|scroll|hidden|clip/.test(style.overflowX)) {
          clip.left = Math.max(clip.left, box.left + parent.clientLeft);
          clip.right = Math.min(clip.right, box.left + parent.clientLeft + parent.clientWidth);
        }
        if (/auto|scroll|hidden|clip/.test(style.overflowY)) {
          clip.top = Math.max(clip.top, box.top + parent.clientTop);
          clip.bottom = Math.min(clip.bottom, box.top + parent.clientTop + parent.clientHeight);
        }
      }
      const overflowY = element.scrollHeight > Math.ceil(rect.height), overflowX = element.scrollWidth > Math.ceil(rect.width);
      // Read resolved CSS lengths (including rem / WebView zoom), never physical
      // pixels or a duplicated set of JS constants for the hitbox and thumb.
      const sizes = axes.map(axis => {
        const railStyle = root.getComputedStyle(axis.rail), thumbStyle = root.getComputedStyle(axis.thumb);
        return { breadth: parseFloat(axis.vertical ? railStyle.width : railStyle.height),
          inset: parseFloat(axis.vertical ? thumbStyle.right : thumbStyle.bottom),
          minimum: parseFloat(axis.vertical ? thumbStyle.minHeight : thumbStyle.minWidth) };
      });
      axes.forEach((axis, index) => {
        const { vertical, rail, thumb } = axis;
        const { breadth, inset, minimum } = sizes[index], other = sizes[1 - index];
        const corner = (vertical ? overflowX : overflowY) ? other.breadth - inset : 0;
        const length = Math.max(0, (vertical ? rect.height : rect.width) - inset * 2 - corner);
        const left = rect.left + (vertical ? rect.width - breadth : inset), top = rect.top + (vertical ? inset : rect.height - breadth);
        const width = vertical ? breadth : length, height = vertical ? length : breadth;
        rail.hidden = !visible || !(vertical ? overflowY : overflowX) || length <= 0 ||
          left + width <= clip.left || left >= clip.right || top + height <= clip.top || top >= clip.bottom;
        if (rail.hidden) return;
        axis.start = vertical ? top : left;
        axis.geometry = metrics(vertical ? rect.height : rect.width,
          vertical ? element.scrollHeight : element.scrollWidth, vertical ? element.scrollTop : element.scrollLeft, length, minimum);
        rail.style.left = left + 'px'; rail.style.top = top + 'px';
        rail.style[vertical ? 'height' : 'width'] = length + 'px';
        rail.style.clipPath = `inset(${Math.max(0, clip.top - top)}px ${Math.max(0, left + width - clip.right)}px ${Math.max(0, top + height - clip.bottom)}px ${Math.max(0, clip.left - left)}px)`;
        thumb.style[vertical ? 'height' : 'width'] = axis.geometry.size + 'px';
        thumb.style.transform = `translate${vertical ? 'Y' : 'X'}(${axis.geometry.position}px)`;
        rail.setAttribute('aria-valuemax', String(Math.round(axis.geometry.range)));
        rail.setAttribute('aria-valuenow', String(Math.round(vertical ? element.scrollTop : element.scrollLeft)));
      });
    }

    const resize = new root.ResizeObserver(schedule);
    doc.querySelectorAll('[data-scroll-area]').forEach((element, index) => {
      if (!element.id) element.id = 'scroll-area-' + index;
      const label = element.dataset.scrollArea || element.getAttribute('aria-label') || '内容';
      const entry = { element, axes: [makeAxis(element, 'vertical', label), makeAxis(element, 'horizontal', label)] };
      entries.push(entry); resize.observe(element);
      element.addEventListener('scroll', () => { entry.axes.forEach(axis => axis.pulse()); schedule(); }, { passive: true });
    });
    // Ignore our own geometry writes to avoid a mutation / animation-frame loop.
    new root.MutationObserver(records => {
      if (records.some(record => !record.target.closest?.('[data-scroll-overlay]'))) schedule();
    }).observe(doc.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['class', 'style', 'hidden', 'open'] });
    doc.addEventListener('scroll', schedule, { passive: true, capture: true });
    root.addEventListener('resize', schedule);
    doc.fonts?.ready.then(schedule);
    schedule();
  }

  if (typeof module !== 'undefined' && module.exports) module.exports = { metrics, offsetForThumb };
  if (root.document) install(root.document);
})(typeof window !== 'undefined' ? window : globalThis);
