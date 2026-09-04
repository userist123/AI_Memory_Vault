"""
Spike-Timing-Dependent Plasticity (STDP) Synapse for Neuromorphic Substrate.

Theoretical Foundation:
Biologically plausible Hebbian learning rule where synaptic weight W updates based
on the relative timing between pre-synaptic and post-synaptic spikes (delta_t = t_post - t_pre):
    - Long-Term Potentiation (LTP): delta_t > 0 => dW = A_pos * exp(-delta_t / tau_pos)
    - Long-Term Depression (LTD):  delta_t < 0 => dW = -A_neg * exp(delta_t / tau_neg)
"""

import math
from typing import Optional

class STDPSynapse:
    """
    Biological STDP plasticity synapse connecting pre-synaptic and post-synaptic LIF neurons.
    """
    def __init__(
        self,
        pre_id: str,
        post_id: str,
        initial_weight: float = 0.5,
        w_min: float = 0.0,
        w_max: float = 1.0,
        a_pos: float = 0.1,    # LTP amplitude
        a_neg: float = 0.12,   # LTD amplitude
        tau_pos: float = 20.0, # LTP time constant (ms)
        tau_neg: float = 20.0  # LTD time constant (ms)
    ):
        self.pre_id: str = pre_id
        self.post_id: str = post_id
        self.weight: float = initial_weight
        self.w_min: float = w_min
        self.w_max: float = w_max
        self.a_pos: float = a_pos
        self.a_neg: float = a_neg
        self.tau_pos: float = tau_pos
        self.tau_neg: float = tau_neg

        self.last_pre_spike: Optional[float] = None
        self.last_post_spike: Optional[float] = None

    def on_pre_spike(self, t_pre: float) -> float:
        """
        Calculates LTD when pre-synaptic spike arrives after post-synaptic spike.
        """
        self.last_pre_spike = t_pre
        if self.last_post_spike is not None:
            delta_t = t_pre - self.last_post_spike  # Negative delta_t
            dw = -self.a_neg * math.exp(delta_t / self.tau_neg)
            self.weight = max(self.w_min, min(self.w_max, self.weight + dw))
        return self.weight

    def on_post_spike(self, t_post: float) -> float:
        """
        Calculates LTP when post-synaptic spike arrives after pre-synaptic spike.
        """
        self.last_post_spike = t_post
        if self.last_pre_spike is not None:
            delta_t = t_post - self.last_pre_spike  # Positive delta_t
            dw = self.a_pos * math.exp(-delta_t / self.tau_pos)
            self.weight = max(self.w_min, min(self.w_max, self.weight + dw))
        return self.weight
