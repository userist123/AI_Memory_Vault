"""
Spiking Memory Network Bridge for Neuromorphic Substrate.

Links symbolic MemoryController note IDs to sub-symbolic LIF spiking neurons
and STDP plastic synapses, providing biological temporal simulation dynamics.
"""

from typing import Dict, List, Tuple, Optional
from .lif_neuron import LIFNeuron
from .stdp_synapse import STDPSynapse

class SpikingMemoryNetwork:
    """
    Experimental sub-symbolic spiking neural network representing memory concepts.
    """
    def __init__(self):
        self.neurons: Dict[str, LIFNeuron] = {}
        self.synapses: Dict[Tuple[str, str], STDPSynapse] = {}

    def add_memory_neuron(self, note_id: str) -> LIFNeuron:
        if note_id not in self.neurons:
            self.neurons[note_id] = LIFNeuron(neuron_id=note_id)
        return self.neurons[note_id]

    def connect_synapse(self, pre_id: str, post_id: str, weight: float = 0.5) -> STDPSynapse:
        self.add_memory_neuron(pre_id)
        self.add_memory_neuron(post_id)
        key = (pre_id, post_id)
        if key not in self.synapses:
            self.synapses[key] = STDPSynapse(pre_id=pre_id, post_id=post_id, initial_weight=weight)
        return self.synapses[key]

    def simulate_step(self, current_inputs: Dict[str, float], dt: float = 1.0, time_step: float = 0.0) -> Dict[str, bool]:
        """
        Simulates one time step of the SNN network.
        Returns a dict mapping note_id to boolean spike status.
        """
        spikes: Dict[str, bool] = {}

        # 1. Update neurons with input currents + synaptic currents
        for note_id, neuron in self.neurons.items():
            base_input = current_inputs.get(note_id, 0.0)
            
            # Sum incoming synaptic currents from spiking pre-neurons
            synaptic_current = 0.0
            for (pre_id, post_id), syn in self.synapses.items():
                if post_id == note_id and pre_id in spikes and spikes[pre_id]:
                    synaptic_current += syn.weight * 20.0  # Synaptic current injection
                    
            spiked = neuron.step(current_input=base_input + synaptic_current, dt=dt, current_time=time_step)
            spikes[note_id] = spiked

        # 2. Update STDP synaptic weights based on spike timing
        for note_id, spiked in spikes.items():
            if spiked:
                # Update pre-synapses and post-synapses
                for (pre_id, post_id), syn in self.synapses.items():
                    if pre_id == note_id:
                        syn.on_pre_spike(time_step)
                    if post_id == note_id:
                        syn.on_post_spike(time_step)

        return spikes
