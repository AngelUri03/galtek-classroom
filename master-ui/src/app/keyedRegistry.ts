/** Small deterministic registry used to replace, rather than stack, keyed UI feedback. */
export class KeyedRegistry<T> {
  private readonly values = new Map<string, T>();

  get(key: string) {
    return this.values.get(key);
  }

  replace(key: string, value: T) {
    const previous = this.values.get(key);
    this.values.set(key, value);
    return previous;
  }

  delete(key: string) {
    return this.values.delete(key);
  }

  deleteIfCurrent(key: string, value: T) {
    if (this.values.get(key) !== value) return false;
    this.values.delete(key);
    return true;
  }

  get size() {
    return this.values.size;
  }
}
