"""
Leaky Integrate-and-Fire (LIF) Neuron Model for Neuromorphic Substrate.

Theoretical Foundation:
Differential equation describing membrane potential V dynamics:
    tau * dV/dt = -(V - V_rest) + R * I(t)

When V >= V_th:
    Spike emitted = True
    V resets to V_reset
    Neuron enters refractory period for t_ref steps.
"""

from typing import Optional, List

class LIFNeuron:
    """
    Leaky Integrate-and-Fire (LIF) biological neuron simulation model.
    """
    def __init__(
        self,
        neuron_id: str,
        v_rest: float = -70.0,    # Resting membrane potential (mV)
        v_reset: float = -70.0,   # Reset membrane potential (mV)
        v_th: float = -55.0,      # Firing threshold potential (mV)
        tau: float = 10.0,        # Membrane time constant (ms)
        resistance: float = 1.0,  # Membrane resistance (Mohm)
        refractory_period: int = 2 # Refractory period in time steps
    ):
        self.neuron_id: str = neuron_id
        self.v_rest: float = v_rest
        self.v_reset: float = v_reset
        self.v_th: float = v_th
        self.tau: float = tau
        self.resistance: float = resistance
        self.refractory_period: int = refractory_period

        self.v: float = v_rest
        self.refractory_counter: int = 0
        self.spike_times: List[float] = []

    def step(self, current_input: float, dt: float = 1.0, current_time: Optional[float] = None) -> bool:
        """
        Simulates one time step of the LIF neuron dynamics.
        Returns True if a spike occurred, False otherwise.
        """
        if self.refractory_counter > 0:
            self.refractory_counter -= 1
            self.v = self.v_reset
            return False

        # Leaky integrate differential equation discrete step
        dv = (-(self.v - self.v_rest) + self.resistance * current_input) / self.tau * dt
        self.v += dv

        # Check threshold firing
        if self.v >= self.v_th:
            self.v = self.v_reset
            self.refractory_counter = self.refractory_period
            t = current_time if current_time is not None else len(self.spike_times) * dt
            self.spike_times.append(t)
            return True

        return False

    def reset(self):
        self.v = self.v_rest
        self.refractory_counter = 0
        self.spike_times.clear()
