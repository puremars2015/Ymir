import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ApiService } from '../api/api.service';
import { ModelStore } from './model.store';

describe('thinking preference', () => {
  beforeEach(() => {
    localStorage.removeItem('ymir.preferredThinking');
    TestBed.configureTestingModule({
      providers: [{ provide: ApiService, useValue: { listModels: () => of([]) } }],
    });
  });
  afterEach(() => localStorage.removeItem('ymir.preferredThinking'));

  it('uses the provider default when there is no saved preference', () => {
    expect(TestBed.inject(ModelStore).depth()).toBeNull();
  });

  it('restores a valid preference and ignores unknown saved values', () => {
    localStorage.setItem('ymir.preferredThinking', 'unexpected');
    expect(TestBed.inject(ModelStore).depth()).toBeNull();
  });

  it('saves explicit depth and can return to the provider default', () => {
    localStorage.setItem('ymir.preferredThinking', 'high');
    const store = TestBed.inject(ModelStore);
    expect(store.depth()).toBe('high');
    store.rememberDepth('low');
    expect(localStorage.getItem('ymir.preferredThinking')).toBe('low');
    store.rememberDepth('invalid');
    expect(store.depth()).toBe('low');
    store.rememberDepth(null);
    expect(store.depth()).toBeNull();
  });

  it('never sends a remembered depth to a model without the declared capability', () => {
    const store = TestBed.inject(ModelStore);
    store.models.set([
      {
        id: 'fixed',
        displayName: 'Fixed',
        supportsImages: false,
        supportsThinking: false,
        allowKnowledgeBase: false,
        isDefault: true,
      },
      {
        id: 'thinking',
        displayName: 'Thinking',
        supportsImages: false,
        supportsThinking: true,
        allowKnowledgeBase: false,
        isDefault: false,
      },
    ]);
    store.rememberDepth('high');
    expect(store.thinkingFor('thinking')).toBe('high');
    expect(store.thinkingFor('fixed')).toBeNull();
    expect(store.thinkingFor('removed')).toBeNull();
    store.models.set([]);
    expect(store.thinkingFor('thinking')).toBeNull();
  });
});
