import { toSlides, type Slide } from './slides';

/** Presentation mode (Release 1.1): which note is being presented, and where. */
class Presenter {
  title = $state('');
  slides = $state<Slide[]>([]);
  index = $state(0);
  active = $state(false);

  open(title: string, body: string) {
    this.title = title;
    this.slides = toSlides(title, body);
    this.index = 0;
    this.active = true;
  }

  close() {
    this.active = false;
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
  }

  go(i: number) { this.index = Math.max(0, Math.min(this.slides.length - 1, i)); }
}

export const presenter = new Presenter();
