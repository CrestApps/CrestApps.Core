// Runs before the model's animation script. It gives the script a canvas-like `ctx` that records every drawing
// call into a flat list of numbers, which the host replays onto the real canvas once per frame. Recording keeps
// the script from ever touching a host object: only numbers and strings cross the boundary.
"use strict";

// Declared with var so they are properties of the global object, where the host reads the strings from.
var __ops = [];
var __strings = [];
const __stringIndex = new Map();
let __state;
let __time = 0;

function __str(value) {
    const text = String(value);
    let index = __stringIndex.get(text);

    if (index === undefined) {
        index = __strings.length;
        __strings.push(text);
        __stringIndex.set(text, index);
    }

    return index;
}

function __num(value) {
    const number = +value;

    return Number.isFinite(number) ? number : 0;
}

// Math.random and Date.now are made deterministic, so the same script always draws the same video.
let __seed = (__info.seed >>> 0) || 7;

Math.random = function () {
    __seed = (__seed + 0x6D2B79F5) >>> 0;
    let value = __seed;
    value = Math.imul(value ^ (value >>> 15), value | 1);
    value ^= value + Math.imul(value ^ (value >>> 7), value | 61);

    return ((value ^ (value >>> 14)) >>> 0) / 4294967296;
};

Date.now = function () {
    return Math.round(__time * 1000);
};

class CanvasGradient {
    constructor(kind, coordinates) {
        this.kind = kind;
        this.coordinates = coordinates;
        this.stops = [];
    }

    addColorStop(offset, color) {
        this.stops.push(Math.min(1, Math.max(0, __num(offset))), __str(color));
    }
}

const __compositeModes = ["source-over", "lighter", "multiply", "screen", "overlay", "darken", "lighten",
    "color-dodge", "color-burn", "hard-light", "soft-light", "difference", "exclusion", "source-atop",
    "destination-out", "destination-over", "source-in", "destination-in", "xor", "copy"];

const __defaults = {
    fillStyle: "#000000", strokeStyle: "#000000", lineWidth: 1, lineCap: "butt", lineJoin: "miter",
    globalAlpha: 1, globalCompositeOperation: "source-over", filter: "none", shadowColor: "rgba(0,0,0,0)",
    shadowBlur: 0, shadowOffsetX: 0, shadowOffsetY: 0, font: "10px sans-serif", textAlign: "start",
};

function __emitStyle(opColor, opGradient, value) {
    if (value instanceof CanvasGradient) {
        __ops.push(opGradient, value.kind, ...value.coordinates, value.stops.length / 2, ...value.stops);
    } else {
        __ops.push(opColor, __str(value));
    }
}

function __emitShadow(values) {
    __ops.push(40, __str(values.shadowColor), __num(values.shadowBlur), __num(values.shadowOffsetX), __num(values.shadowOffsetY));
}

function __parseFont(font) {
    const sizeMatch = /(\d+(?:\.\d+)?)px/.exec(font);

    return {
        size: sizeMatch ? +sizeMatch[1] : 10,
        bold: /\bbold\b|\b[6-9]00\b/.test(font) ? 1 : 0,
        italic: /\bitalic\b/.test(font) ? 1 : 0,
    };
}

function __emitText(op, text, x, y, values) {
    const font = __parseFont(values.font);
    const align = values.textAlign === "center" ? 1 : values.textAlign === "right" || values.textAlign === "end" ? 2 : 0;

    __ops.push(op, __str(text), __num(x), __num(y), font.size, font.bold, font.italic, align);
}

function __createContext(width, height) {
    let values = Object.assign({}, __defaults);
    const stack = [];

    const context = {
        canvas: { width: width, height: height },

        save() { stack.push(Object.assign({}, values)); __ops.push(1); },
        restore() { if (stack.length > 0) { values = stack.pop(); } __ops.push(2); },
        translate(x, y) { __ops.push(3, __num(x), __num(y)); },
        rotate(angle) { __ops.push(4, __num(angle)); },
        scale(x, y) { __ops.push(5, __num(x), __num(y === undefined ? x : y)); },
        transform(a, b, c, d, e, f) { __ops.push(6, __num(a), __num(b), __num(c), __num(d), __num(e), __num(f)); },
        setTransform(a, b, c, d, e, f) { __ops.push(7, __num(a), __num(b), __num(c), __num(d), __num(e), __num(f)); },
        resetTransform() { __ops.push(8); },

        beginPath() { __ops.push(10); },
        closePath() { __ops.push(11); },
        moveTo(x, y) { __ops.push(12, __num(x), __num(y)); },
        lineTo(x, y) { __ops.push(13, __num(x), __num(y)); },
        quadraticCurveTo(cx, cy, x, y) { __ops.push(14, __num(cx), __num(cy), __num(x), __num(y)); },
        bezierCurveTo(c1x, c1y, c2x, c2y, x, y) { __ops.push(15, __num(c1x), __num(c1y), __num(c2x), __num(c2y), __num(x), __num(y)); },
        arc(x, y, radius, start, end, counterclockwise) { __ops.push(16, __num(x), __num(y), Math.abs(__num(radius)), __num(start), __num(end), counterclockwise ? 1 : 0); },
        ellipse(x, y, radiusX, radiusY, rotation, start, end, counterclockwise) { __ops.push(17, __num(x), __num(y), Math.abs(__num(radiusX)), Math.abs(__num(radiusY)), __num(rotation), __num(start), __num(end), counterclockwise ? 1 : 0); },
        rect(x, y, w, h) { __ops.push(18, __num(x), __num(y), __num(w), __num(h)); },
        roundRect(x, y, w, h, radius) { __ops.push(19, __num(x), __num(y), __num(w), __num(h), __num(Array.isArray(radius) ? radius[0] : radius)); },

        fill(rule) { __ops.push(20, rule === "evenodd" ? 1 : 0); },
        stroke() { __ops.push(21); },
        clip(rule) { __ops.push(22, rule === "evenodd" ? 1 : 0); },
        fillRect(x, y, w, h) { __ops.push(23, __num(x), __num(y), __num(w), __num(h)); },
        strokeRect(x, y, w, h) { __ops.push(24, __num(x), __num(y), __num(w), __num(h)); },
        clearRect(x, y, w, h) { __ops.push(25, __num(x), __num(y), __num(w), __num(h)); },
        fillText(text, x, y) { __emitText(41, text, x, y, values); },
        strokeText(text, x, y) { __emitText(42, text, x, y, values); },
        measureText(text) { return { width: String(text).length * __parseFont(values.font).size * 0.55 }; },

        createLinearGradient(x0, y0, x1, y1) { return new CanvasGradient(0, [__num(x0), __num(y0), __num(x1), __num(y1), 0, 0]); },
        createRadialGradient(x0, y0, r0, x1, y1, r1) { return new CanvasGradient(1, [__num(x0), __num(y0), Math.abs(__num(r0)), __num(x1), __num(y1), Math.abs(__num(r1))]); },

        get fillStyle() { return values.fillStyle; },
        set fillStyle(value) { values.fillStyle = value; __emitStyle(30, 32, value); },
        get strokeStyle() { return values.strokeStyle; },
        set strokeStyle(value) { values.strokeStyle = value; __emitStyle(31, 33, value); },
        get lineWidth() { return values.lineWidth; },
        set lineWidth(value) { values.lineWidth = __num(value); __ops.push(34, values.lineWidth); },
        get lineCap() { return values.lineCap; },
        set lineCap(value) { values.lineCap = value; __ops.push(35, value === "round" ? 1 : value === "square" ? 2 : 0); },
        get lineJoin() { return values.lineJoin; },
        set lineJoin(value) { values.lineJoin = value; __ops.push(36, value === "round" ? 1 : value === "bevel" ? 2 : 0); },
        get globalAlpha() { return values.globalAlpha; },
        set globalAlpha(value) { values.globalAlpha = Math.min(1, Math.max(0, __num(value))); __ops.push(37, values.globalAlpha); },
        get globalCompositeOperation() { return values.globalCompositeOperation; },
        set globalCompositeOperation(value) { const mode = __compositeModes.indexOf(value); if (mode >= 0) { values.globalCompositeOperation = value; __ops.push(38, mode); } },
        get filter() { return values.filter; },
        set filter(value) { values.filter = value; const blur = /blur\(\s*(\d+(?:\.\d+)?)px\s*\)/.exec(String(value)); __ops.push(39, blur ? +blur[1] : 0); },
        get shadowColor() { return values.shadowColor; },
        set shadowColor(value) { values.shadowColor = value; __emitShadow(values); },
        get shadowBlur() { return values.shadowBlur; },
        set shadowBlur(value) { values.shadowBlur = __num(value); __emitShadow(values); },
        get shadowOffsetX() { return values.shadowOffsetX; },
        set shadowOffsetX(value) { values.shadowOffsetX = __num(value); __emitShadow(values); },
        get shadowOffsetY() { return values.shadowOffsetY; },
        set shadowOffsetY(value) { values.shadowOffsetY = __num(value); __emitShadow(values); },
        get font() { return values.font; },
        set font(value) { values.font = String(value); },
        get textAlign() { return values.textAlign; },
        set textAlign(value) { values.textAlign = String(value); },
    };

    context.__reset = function () {
        values = Object.assign({}, __defaults);
        stack.length = 0;
    };

    return context;
}

// Drawing helpers the script can use, so organic subjects are drawn from smooth outlines rather than stacked
// primitives. Frozen, so a script cannot break them for itself by accident.
function __points(points) {
    if (points.length > 0 && Array.isArray(points[0])) {
        return points.map(function (point) { return { x: __num(point[0]), y: __num(point[1]) }; });
    }

    const result = [];

    for (let index = 0; index + 1 < points.length; index += 2) {
        result.push({ x: __num(points[index]), y: __num(points[index + 1]) });
    }

    return result;
}

function __hex(color) {
    const text = String(color).trim();
    let match = /^#([0-9a-f]{3})$/i.exec(text);

    if (match) {
        return [0, 1, 2].map(function (index) { return parseInt(match[1][index] + match[1][index], 16); });
    }

    match = /^#([0-9a-f]{6})$/i.exec(text);

    return match ? [0, 2, 4].map(function (index) { return parseInt(match[1].substr(index, 2), 16); }) : [128, 128, 128];
}

function __toHex(channels) {
    return "#" + channels.map(function (value) { return Math.round(Math.min(255, Math.max(0, value))).toString(16).padStart(2, "0"); }).join("");
}

let __noiseSeed = (__info.seed >>> 0) || 7;
const __noiseTable = [];

for (let index = 0; index < 256; index++) {
    __noiseSeed = (Math.imul(__noiseSeed, 1664525) + 1013904223) >>> 0;
    __noiseTable.push(__noiseSeed / 4294967296);
}

const draw = Object.freeze({
    smoothShape(ctx, points, closed, tension) {
        const list = __points(points);
        const count = list.length;
        const amount = (tension === undefined ? 0.5 : __num(tension)) / 3 * 2;
        const isClosed = closed !== false;

        if (count < 2) {
            return;
        }

        ctx.moveTo(list[0].x, list[0].y);

        const segments = isClosed ? count : count - 1;

        for (let index = 0; index < segments; index++) {
            const previous = list[isClosed ? (index - 1 + count) % count : Math.max(index - 1, 0)];
            const current = list[index];
            const next = list[(index + 1) % count];
            const after = list[isClosed ? (index + 2) % count : Math.min(index + 2, count - 1)];

            ctx.bezierCurveTo(
                current.x + (next.x - previous.x) * amount / 2, current.y + (next.y - previous.y) * amount / 2,
                next.x - (after.x - current.x) * amount / 2, next.y - (after.y - current.y) * amount / 2,
                next.x, next.y);
        }

        if (isClosed) {
            ctx.closePath();
        }
    },

    limb(ctx, x1, y1, x2, y2, startWidth, endWidth) {
        const angle = Math.atan2(y2 - y1, x2 - x1);
        const normalX = -Math.sin(angle);
        const normalY = Math.cos(angle);
        const startRadius = __num(startWidth) / 2;
        const endRadius = __num(endWidth === undefined ? startWidth : endWidth) / 2;

        ctx.moveTo(x1 + normalX * startRadius, y1 + normalY * startRadius);
        ctx.lineTo(x2 + normalX * endRadius, y2 + normalY * endRadius);
        ctx.arc(x2, y2, endRadius, angle + Math.PI / 2, angle - Math.PI / 2, true);
        ctx.lineTo(x1 - normalX * startRadius, y1 - normalY * startRadius);
        ctx.arc(x1, y1, startRadius, angle - Math.PI / 2, angle + Math.PI / 2, true);
        ctx.closePath();
    },

    shade(color, amount) {
        const channels = __hex(color);
        const value = __num(amount);

        return __toHex(channels.map(function (channel) { return value >= 0 ? channel + (255 - channel) * value : channel * (1 + value); }));
    },

    mix(first, second, amount) {
        const from = __hex(first);
        const to = __hex(second);
        const value = Math.min(1, Math.max(0, __num(amount)));

        return __toHex(from.map(function (channel, index) { return channel + (to[index] - channel) * value; }));
    },

    noise(x) {
        const floor = Math.floor(__num(x));
        const fraction = __num(x) - floor;
        const smooth = fraction * fraction * (3 - 2 * fraction);
        const first = __noiseTable[((floor % 256) + 256) % 256];
        const second = __noiseTable[(((floor + 1) % 256) + 256) % 256];

        return (first + (second - first) * smooth) * 2 - 1;
    },

    rgba(color, alpha) {
        const channels = __hex(color);

        return "rgba(" + channels[0] + "," + channels[1] + "," + channels[2] + "," + Math.min(1, Math.max(0, __num(alpha))) + ")";
    },

    gradient(ctx, x0, y0, x1, y1, stops) {
        const gradient = ctx.createLinearGradient(x0, y0, x1, y1);

        stops.forEach(function (stop) { gradient.addColorStop(stop[0], stop[1]); });

        return gradient;
    },

    radialGradient(ctx, x, y, innerRadius, outerRadius, stops) {
        const gradient = ctx.createRadialGradient(x, y, innerRadius, x, y, outerRadius);

        stops.forEach(function (stop) { gradient.addColorStop(stop[0], stop[1]); });

        return gradient;
    },

    sky(ctx, width, horizonY, colors) {
        const list = colors && colors.length > 1 ? colors : ["#35406e", "#d9825a", "#ffd48a"];

        ctx.fillStyle = draw.gradient(ctx, 0, 0, 0, horizonY, list.map(function (color, index) { return [index / (list.length - 1), color]; }));
        ctx.fillRect(-width, -horizonY, width * 3, horizonY * 3);
    },

    glow(ctx, x, y, radius, color, strength) {
        const amount = strength === undefined ? 0.8 : __num(strength);

        ctx.save();
        ctx.globalCompositeOperation = "lighter";
        ctx.fillStyle = draw.radialGradient(ctx, x, y, 0, radius, [[0, draw.rgba(color, amount)], [0.15, draw.rgba(color, amount * 0.6)], [1, draw.rgba(color, 0)]]);
        ctx.fillRect(x - radius, y - radius, radius * 2, radius * 2);
        ctx.restore();
    },

    ridge(ctx, width, baseY, amplitude, color, options) {
        const settings = options || {};
        const roughness = settings.roughness === undefined ? 1 : __num(settings.roughness);
        const offset = __num(settings.seed) * 37.1;

        ctx.save();
        ctx.filter = settings.blur ? "blur(" + __num(settings.blur) + "px)" : "none";
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.moveTo(-width, baseY + amplitude * 4 + 2000);

        for (let x = -width; x <= width * 2; x += 12) {
            const shape = Math.sin(x * 0.004 + offset) * 0.6 + draw.noise(x * 0.008 * roughness + offset) * 0.4;

            ctx.lineTo(x, baseY - amplitude * (0.5 + 0.5 * shape));
        }

        ctx.lineTo(width * 2, baseY + amplitude * 4 + 2000);
        ctx.closePath();
        ctx.fill();
        ctx.restore();
    },

    shadow(ctx, x, y, radiusX, radiusY, opacity) {
        ctx.save();
        ctx.filter = "blur(" + Math.max(2, radiusY * 0.6) + "px)";
        ctx.fillStyle = "rgba(0,0,0," + (opacity === undefined ? 0.35 : __num(opacity)) + ")";
        ctx.beginPath();
        ctx.ellipse(x, y, radiusX, radiusY, 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
    },

    rays(ctx, x, y, length, color, t, count) {
        const total = count === undefined ? 9 : count;

        ctx.save();
        ctx.globalCompositeOperation = "lighter";

        for (let index = 0; index < total; index++) {
            const angle = Math.PI * (0.55 + index / total) + Math.sin(t * 0.15 + index) * 0.03;
            const spread = 0.05 + 0.025 * draw.noise(index * 3.1);

            ctx.fillStyle = draw.gradient(ctx, x, y, x + Math.cos(angle) * length, y + Math.sin(angle) * length, [[0, draw.rgba(color, 0.07)], [0.5, draw.rgba(color, 0.025)], [1, draw.rgba(color, 0)]]);
            ctx.beginPath();
            ctx.moveTo(x, y);
            ctx.lineTo(x + Math.cos(angle - spread) * length, y + Math.sin(angle - spread) * length);
            ctx.lineTo(x + Math.cos(angle + spread) * length, y + Math.sin(angle + spread) * length);
            ctx.closePath();
            ctx.fill();
        }

        ctx.restore();
    },

    camera(ctx, focusX, focusY, zoom, shakeX, shakeY) {
        ctx.translate(focusX + __num(shakeX), focusY + __num(shakeY));
        ctx.scale(zoom, zoom);
        ctx.translate(-focusX, -focusY);
    },

    makeGrass(width, horizonY, height, count, colors) {
        const palette = colors && colors.length > 1 ? colors : ["#8a6a2a", "#e8c46a"];
        const blades = [];

        for (let index = 0; index < count; index++) {
            const depth = Math.random();

            blades.push({
                x: Math.random() * width * 1.3 - width * 0.15,
                y: horizonY + (height - horizonY) * (0.05 + depth * 1.0),
                height: (height - horizonY) * (0.06 + depth * 0.32) * (0.7 + Math.random() * 0.6),
                width: 0.6 + depth * 2.6,
                depth: depth,
                phase: Math.random() * Math.PI * 2,
                color: draw.mix(palette[0], palette[palette.length - 1], Math.random()),
            });
        }

        return blades.sort(function (a, b) { return a.depth - b.depth; });
    },

    grass(ctx, blades, t, options) {
        const settings = options || {};
        const from = settings.fromDepth === undefined ? 0 : settings.fromDepth;
        const to = settings.toDepth === undefined ? 1 : settings.toDepth;
        const wind = settings.wind === undefined ? 0.12 : settings.wind;
        const lean = settings.lean === undefined ? 0.06 : settings.lean;

        ctx.save();
        ctx.lineCap = "round";

        for (const blade of blades) {
            if (blade.depth < from || blade.depth >= to) {
                continue;
            }

            const sway = (Math.sin(t * 1.8 + blade.phase + blade.x * 0.004) * wind + lean) * blade.height;

            ctx.filter = blade.depth > 0.93 ? "blur(2px)" : "none";
            ctx.strokeStyle = blade.color;
            ctx.lineWidth = blade.width;
            ctx.beginPath();
            ctx.moveTo(blade.x, blade.y);
            ctx.quadraticCurveTo(blade.x + sway * 0.3, blade.y - blade.height * 0.6, blade.x + sway, blade.y - blade.height);
            ctx.stroke();
        }

        ctx.restore();
    },

    clamp(value, low, high) { return Math.min(high, Math.max(low, value)); },
    lerp(from, to, amount) { return from + (to - from) * amount; },
    smoothstep(edge0, edge1, value) { const x = Math.min(1, Math.max(0, (value - edge0) / (edge1 - edge0))); return x * x * (3 - 2 * x); },
    easeInOut(p) { const x = Math.min(1, Math.max(0, p)); return x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2; },
    easeOut(p) { const x = Math.min(1, Math.max(0, p)); return 1 - Math.pow(1 - x, 3); },
    easeIn(p) { const x = Math.min(1, Math.max(0, p)); return x * x * x; },
    easeOutBack(p) { const x = Math.min(1, Math.max(0, p)); return 1 + 2.70158 * Math.pow(x - 1, 3) + 1.70158 * Math.pow(x - 1, 2); },
});

// Named so it cannot collide with a `ctx` the script declares itself; the script receives it as frame's first
// argument.
const __ctx = __createContext(__info.width, __info.height);

function __setup() {
    __state = typeof setup === "function" ? setup(__info) : {};

    if (__state === undefined || __state === null) {
        __state = {};
    }
}

function __frame(time) {
    if (typeof frame !== "function") {
        throw new Error("The script must define function frame(ctx, t, state).");
    }

    __ops.length = 0;
    __strings.length = 0;
    __stringIndex.clear();
    __time = time;
    __ctx.__reset();
    frame(__ctx, time, __state);

    return __ops;
}
