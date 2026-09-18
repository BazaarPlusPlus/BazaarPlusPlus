export class FakeClock {
  constructor(public ms: number) {}

  readonly now = (): number => this.ms;

  advance(delta: number): void {
    this.ms += delta;
  }
}
