import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/app.css'
import { App } from './App'

const container = document.getElementById('root')
if (!container) {
  throw new Error('No #root element. index.html and this entry point disagree about the mount point.')
}

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
