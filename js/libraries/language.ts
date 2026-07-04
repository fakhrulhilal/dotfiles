export type Guard<T> = (value: unknown) => value is T;

/**
 * Trusts the inner shape (no deep validation) – use only when you accept the risk.
 * Useful for endpoints where the payload is huge and a deep guard is impractical.
 */
export function trustShape<T>(): Guard<T> {
    return (_value): _value is T => true;
}
