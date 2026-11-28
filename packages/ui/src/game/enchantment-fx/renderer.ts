import {
  fragment,
  vertex,
  type CardFx,
  type FxMaterial,
  type FxTexture
} from './program';
import type { Enchantment } from '../types';

function compile(gl: WebGL2RenderingContext, source: string, type: number) {
  const shader = gl.createShader(type);
  if (!shader) throw new Error('Shader unavailable');
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
    const reason = gl.getShaderInfoLog(shader);
    gl.deleteShader(shader);
    throw new Error(reason ?? 'Shader compilation failed');
  }
  return shader;
}

function link(gl: WebGL2RenderingContext, source: string) {
  const program = gl.createProgram();
  const shaders: WebGLShader[] = [];
  try {
    shaders.push(compile(gl, vertex, gl.VERTEX_SHADER));
    shaders.push(compile(gl, source, gl.FRAGMENT_SHADER));
    for (const shader of shaders) gl.attachShader(program, shader);
    gl.linkProgram(program);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS))
      throw new Error(gl.getProgramInfoLog(program) ?? 'Shader link failed');
  } catch (error) {
    gl.deleteProgram(program);
    throw error;
  } finally {
    for (const shader of shaders) gl.deleteShader(shader);
  }
  const uniforms = Array.from(
    { length: Number(gl.getProgramParameter(program, gl.ACTIVE_UNIFORMS)) },
    (_, index) => {
      const active = gl.getActiveUniform(program, index);
      if (!active) throw new Error('Uniform unavailable');
      const { type, name } = active;
      return { type, name, location: gl.getUniformLocation(program, name) };
    }
  );
  return { program, uniforms };
}

async function upload(
  gl: WebGL2RenderingContext,
  fx: CardFx,
  binding: FxTexture
) {
  if ('default' in binding) {
    if (binding.default !== 'black' && binding.default !== 'white')
      throw new Error('Unknown default FX texture');
    const texture = gl.createTexture();
    if (!texture) throw new Error('WebGL unavailable');
    const channel = binding.default === 'white' ? 255 : 0;
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(
      gl.TEXTURE_2D,
      0,
      gl.RGBA,
      1,
      1,
      0,
      gl.RGBA,
      gl.UNSIGNED_BYTE,
      new Uint8Array([channel, channel, channel, 255])
    );
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    return texture;
  }
  const pixels = await createImageBitmap(await fx.texture(binding.file), {
    imageOrientation: 'flipY',
    premultiplyAlpha: 'none',
    colorSpaceConversion: 'none'
  });
  const texture = gl.createTexture();
  try {
    if (!texture || gl.isContextLost()) throw new Error('WebGL unavailable');
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(
      gl.TEXTURE_2D,
      0,
      binding.colorSpace === 1 ? gl.SRGB8_ALPHA8 : gl.RGBA8,
      gl.RGBA,
      gl.UNSIGNED_BYTE,
      pixels
    );
    const wrap = [gl.REPEAT, gl.CLAMP_TO_EDGE, gl.MIRRORED_REPEAT];
    gl.texParameteri(
      gl.TEXTURE_2D,
      gl.TEXTURE_WRAP_S,
      wrap[binding.wrap[0]] ?? gl.CLAMP_TO_EDGE
    );
    gl.texParameteri(
      gl.TEXTURE_2D,
      gl.TEXTURE_WRAP_T,
      wrap[binding.wrap[1]] ?? gl.CLAMP_TO_EDGE
    );
    const base = binding.filter === 0 ? gl.NEAREST : gl.LINEAR;
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, base);
    let min: number = base;
    if (binding.mipCount > 1) {
      gl.generateMipmap(gl.TEXTURE_2D);
      gl.texParameteri(
        gl.TEXTURE_2D,
        gl.TEXTURE_MAX_LEVEL,
        binding.mipCount - 1
      );
      min = [
        gl.NEAREST_MIPMAP_NEAREST,
        gl.LINEAR_MIPMAP_NEAREST,
        gl.LINEAR_MIPMAP_LINEAR
      ][binding.filter];
    }
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, min);
    return texture;
  } catch (error) {
    gl.deleteTexture(texture);
    throw error;
  } finally {
    pixels.close();
  }
}

type Face = {
  canvas: HTMLCanvasElement;
  source: CardFx;
  enchant: Enchantment;
  still: boolean;
  visible: boolean;
  loading: boolean;
  dirty: boolean;
  draw?: (time: number) => void;
  releaseTextures: Array<() => void>;
  lastDraw: number;
};

class Renderer {
  buffer = document.createElement('canvas');
  gl: WebGL2RenderingContext | null | undefined;
  faces = new Map<Element, Face>();
  motion = matchMedia('(prefers-reduced-motion: reduce)');
  programs = new Map<string, ReturnType<typeof link>>();
  textures = new Map<string, { refs: number; ready: Promise<WebGLTexture> }>();
  generation = 0;
  frame = 0;
  nextFace = 0;
  intersections = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      const face = this.faces.get(entry.target);
      if (!face) continue;
      face.visible = entry.isIntersecting;
      if (face.visible && !face.loading) void this.prepare(face);
    }
    this.schedule();
  });
  sizes = new ResizeObserver((entries) => {
    for (const entry of entries) {
      const face = this.faces.get(entry.target);
      if (face) face.dirty = true;
    }
    this.schedule();
  });
  constructor() {
    this.buffer.addEventListener('webglcontextlost', (event) => {
      event.preventDefault();
      this.generation++;
      this.programs.clear();
      this.textures.clear();
      for (const face of this.faces.values()) {
        for (const release of face.releaseTextures.splice(0)) release();
        face.canvas.width = 0;
        face.draw = undefined;
        face.dirty = true;
        face.loading = false;
      }
      this.schedule();
    });
    this.buffer.addEventListener('webglcontextrestored', () => {
      for (const face of this.faces.values())
        if (face.visible) void this.prepare(face);
    });
  }
  preference = () => {
    for (const face of this.faces.values()) face.dirty = true;
    this.schedule();
  };
  schedule = () => {
    const needed =
      !document.hidden &&
      [...this.faces.values()].some(
        (face) =>
          face.visible &&
          face.draw &&
          (face.dirty || !(face.still || this.motion.matches))
      );
    if (needed && !this.frame)
      this.frame = requestAnimationFrame((timestamp) => {
        this.frame = 0;
        const faces = [...this.faces.values()];
        const deadline = performance.now() + 6;
        let draws = 0;
        // Resume at the next face when a tick exhausts its copy budget.
        for (let i = 0; i < faces.length; i++) {
          const index = this.nextFace % faces.length;
          this.nextFace = (index + 1) % faces.length;
          const face = faces[index];
          if (!face.visible || !face.draw) continue;
          const still = face.still || this.motion.matches;
          if (!face.dirty && (still || timestamp - face.lastDraw < 32))
            continue;
          face.lastDraw = timestamp;
          face.draw(still ? 1 : timestamp / 1000);
          face.dirty = false;
          if (++draws === 12 || performance.now() >= deadline) break;
        }
        this.schedule();
      });
    if (!needed && this.frame) {
      cancelAnimationFrame(this.frame);
      this.frame = 0;
    }
  };
  async prepare(face: Face) {
    if (this.gl === undefined)
      this.gl = this.buffer.getContext('webgl2', {
        alpha: false,
        antialias: false
      });
    const gl = this.gl;
    if (!gl || gl.isContextLost()) return;
    face.loading = true;
    const generation = this.generation;
    const current = () =>
      generation === this.generation && this.faces.get(face.canvas) === face;
    try {
      const material: FxMaterial = await face.source.material();
      if (!current()) return;
      const templateSlots = { small: 1, medium: 2, large: 3 }[material.size];
      const tinyMode =
        material.size !== 'small' &&
        (!!face.source.tiny ||
          (face.source.slots != null && face.source.slots < templateSlots));
      const source = await fragment(material, face.enchant, tinyMode);
      if (!source || !current()) return;
      const shader = this.programs.get(source) ?? link(gl, source);
      this.programs.set(source, shader);
      const bindings = await Promise.all(
        shader.uniforms.map(async ({ type, name, location }) => {
          if (type === gl.SAMPLER_2D) {
            const binding = material.textures[name];
            if (!binding) throw new Error('Missing FX texture ' + name);
            const key =
              'default' in binding
                ? binding.default
                : JSON.stringify([
                    binding.file,
                    binding.colorSpace,
                    binding.wrap,
                    binding.filter,
                    binding.mipCount
                  ]);
            const entry = this.textures.get(key) ?? {
              refs: 0,
              ready: upload(gl, face.source, binding)
            };
            this.textures.set(key, entry);
            entry.refs++;
            face.releaseTextures.push(() => {
              if (--entry.refs) return;
              if (this.textures.get(key) === entry) this.textures.delete(key);
              void entry.ready.then(
                (texture) => gl.deleteTexture(texture),
                () => {}
              );
            });
            return { location, texture: await entry.ready };
          }
          if (name === 'seconds') return { location };
          const value =
            name === 'aspect'
              ? { small: 0.5, medium: 1, large: 1.5 }[material.size]
              : name.endsWith('_ST')
                ? material.textures[name.slice(0, -3)]?.st
                : material.values[name];
          if (typeof value !== 'number' && !Array.isArray(value))
            throw new Error('Missing FX uniform ' + name);
          return { location, value, type };
        })
      );
      if (!current()) return;
      const main = material.textures['_MainTex'];
      if (!main || 'default' in main) return;
      const ctx = face.canvas.getContext('2d');
      if (!ctx) return;
      face.draw = (seconds) => {
        const { canvas } = face;
        const scale = Math.min(
          1.5,
          devicePixelRatio,
          400 / Math.max(canvas.clientWidth, canvas.clientHeight)
        );
        const width = Math.max(1, Math.round(canvas.clientWidth * scale));
        const height = Math.max(1, Math.round(canvas.clientHeight * scale));
        if (canvas.width !== width) canvas.width = width;
        if (canvas.height !== height) canvas.height = height;
        if (this.buffer.width < width) this.buffer.width = width;
        if (this.buffer.height < height) this.buffer.height = height;
        gl.viewport(0, 0, width, height);
        gl.useProgram(shader.program);
        let unit = 0;
        for (const binding of bindings) {
          if (binding.texture) {
            gl.activeTexture(gl.TEXTURE0 + unit);
            gl.bindTexture(gl.TEXTURE_2D, binding.texture);
            gl.uniform1i(binding.location, unit++);
          } else if (Array.isArray(binding.value)) {
            if (binding.type === gl.FLOAT_VEC4)
              gl.uniform4fv(binding.location, binding.value);
            if (binding.type === gl.FLOAT_VEC3)
              gl.uniform3fv(binding.location, binding.value.slice(0, 3));
            if (binding.type === gl.FLOAT_VEC2)
              gl.uniform2fv(binding.location, binding.value.slice(0, 2));
          } else gl.uniform1f(binding.location, binding.value ?? seconds);
        }
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        ctx.drawImage(
          this.buffer,
          0,
          this.buffer.height - height,
          width,
          height,
          0,
          0,
          width,
          height
        );
      };
      this.schedule();
    } catch {
      // Failed resources leave the underlying illustration visible.
    }
  }
  add(face: Face) {
    if (!this.faces.size) {
      this.motion.addEventListener('change', this.preference);
      document.addEventListener('visibilitychange', this.schedule);
    }
    this.faces.set(face.canvas, face);
    this.intersections.observe(face.canvas);
    this.sizes.observe(face.canvas);
    return () => {
      this.intersections.unobserve(face.canvas);
      this.sizes.unobserve(face.canvas);
      this.faces.delete(face.canvas);
      for (const release of face.releaseTextures.splice(0)) release();
      face.canvas.width = face.canvas.height = 0;
      this.schedule();
      if (!this.faces.size) {
        this.intersections.disconnect();
        this.sizes.disconnect();
        this.motion.removeEventListener('change', this.preference);
        document.removeEventListener('visibilitychange', this.schedule);
        for (const { program } of this.programs.values())
          this.gl?.deleteProgram(program);
        this.buffer.width = this.buffer.height = 0;
        this.textures.clear();
        this.programs.clear();
        this.generation++;
      }
    };
  }
}
let shared: Renderer | undefined;
export function observeFx(
  canvas: HTMLCanvasElement,
  source: CardFx,
  enchant: Enchantment,
  still: boolean
) {
  if (
    typeof IntersectionObserver === 'undefined' ||
    typeof ResizeObserver === 'undefined'
  )
    return;
  shared ??= new Renderer();
  return shared.add({
    canvas,
    source,
    enchant,
    still,
    visible: false,
    dirty: true,
    releaseTextures: [],
    lastDraw: 0,
    loading: false
  });
}
