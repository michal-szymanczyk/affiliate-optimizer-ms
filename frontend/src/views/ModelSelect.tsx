/**
 * Which optimization model decides the next click.
 *
 * It lives in the header rather than beside the feed filters, because it is not a way of looking at
 * the run: it changes what the run does. A switch takes effect from the next click, and every click
 * already decided keeps the model it was recorded with.
 */
export function ModelSelect({
  names,
  active,
  disabled,
  onSelect,
}: {
  names: readonly string[]
  active: string
  disabled: boolean
  onSelect: (name: string) => void
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
      <label
        htmlFor="model"
        style={{ fontSize: 12, color: 'color-mix(in srgb, var(--color-text) 55%, transparent)' }}
      >
        Model
      </label>
      <select
        id="model"
        className="input"
        style={{ width: 150 }}
        value={active}
        disabled={disabled}
        onChange={(event) => onSelect(event.target.value)}
      >
        {names.map((name) => (
          <option key={name} value={name}>
            {name}
          </option>
        ))}
      </select>
    </div>
  )
}
