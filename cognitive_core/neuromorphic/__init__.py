"""
Experimental Neuromorphic Sub-Symbolic Substrate for Cognitive Core.

Theoretical Foundation:
Implements biological spiking neural dynamics using Leaky Integrate-and-Fire (LIF)
neuron models and Spike-Timing-Dependent Plasticity (STDP) learning rules.

Acts as an isolated sub-symbolic research substrate providing biological temporal spiking
dynamics for symbolic memory concepts.
"""

from .lif_neuron import LIFNeuron
from .stdp_synapse import STDPSynapse
from .snn_network import SpikingMemoryNetwork

__all__ = ["LIFNeuron", "STDPSynapse", "SpikingMemoryNetwork"]
