import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { HourlyChart } from './HourlyChart'
import { PatternInput } from './PatternInput'
import { HttpStatus, StatusBadge } from './ui'

describe('StatusBadge', () => {
  it.each([
    ['succeeded', 'Entregue'],
    ['scheduled', 'Reagendada'],
    ['retrying', 'Reagendada'],
    ['dead', 'Morta'],
  ] as const)('mostra %s como "%s"', (status, label) => {
    render(<StatusBadge status={status} />)
    expect(screen.getByText(label)).toBeInTheDocument()
  })
})

describe('HttpStatus', () => {
  it('mostra "rede" quando nao houve resposta HTTP', () => {
    render(<HttpStatus code={null} />)
    expect(screen.getByText('rede')).toBeInTheDocument()
  })
})

function ControlledPatternInput({ initial = [] as string[] }) {
  const [value, setValue] = useState(initial)
  return (
    <>
      <PatternInput value={value} onChange={setValue} />
      <output data-testid="value">{value.join(',')}</output>
    </>
  )
}

describe('PatternInput', () => {
  it('adiciona padroes validos com Enter e normaliza para minusculas', async () => {
    const user = userEvent.setup()
    render(<ControlledPatternInput />)

    await user.type(screen.getByLabelText('Padrões de evento'), 'Order.*{Enter}invoice.#{Enter}')

    expect(screen.getByTestId('value')).toHaveTextContent('order.*,invoice.#')
  })

  it('recusa padrao invalido e explica o motivo', async () => {
    const user = userEvent.setup()
    render(<ControlledPatternInput />)

    await user.type(screen.getByLabelText('Padrões de evento'), 'order..paid{Enter}')

    expect(screen.getByText(/não é um padrão válido/)).toBeInTheDocument()
    expect(screen.getByTestId('value')).toHaveTextContent('')
  })

  it('remove um padrao pelo botao do chip', async () => {
    const user = userEvent.setup()
    render(<ControlledPatternInput initial={['order.*', '#']} />)

    await user.click(screen.getByLabelText('Remover order.*'))

    expect(screen.getByTestId('value')).toHaveTextContent('#')
  })
})

describe('HourlyChart', () => {
  it('desenha uma barra empilhada por hora', () => {
    const series = Array.from({ length: 24 }, (_, i) => ({
      hour: new Date(Date.UTC(2026, 9, 5, i)).toISOString(),
      succeeded: i * 10,
      failed: i,
      dead: 0,
    }))

    const { container } = render(<HourlyChart series={series} />)

    expect(screen.getByRole('img', { name: /por hora/ })).toBeInTheDocument()
    expect(container.querySelectorAll('g')).toHaveLength(24)
  })
})
