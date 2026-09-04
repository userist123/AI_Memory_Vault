import pytest
from cognitive_core.neuromorphic import LIFNeuron, STDPSynapse, SpikingMemoryNetwork

def test_lif_neuron_threshold_spiking():
    neuron = LIFNeuron("test_neuron", v_rest=-70.0, v_th=-55.0, resistance=1.0)
    
    # Low current input should not fire
    spiked = neuron.step(current_input=5.0, dt=1.0, current_time=1.0)
    assert spiked is False
    assert neuron.v > -70.0

    # Strong current input over multiple steps should reach threshold and fire
    spike_occurred = False
    for t in range(20):
        if neuron.step(current_input=30.0, dt=1.0, current_time=float(t)):
            spike_occurred = True
            break
            
    assert spike_occurred is True, "Strong input current must trigger LIF threshold spike"

def test_stdp_synapse_ltp():
    synapse = STDPSynapse(pre_id="pre", post_id="post", initial_weight=0.5)

    # Pre-spike at t=10.0
    synapse.on_pre_spike(t_pre=10.0)

    # Post-spike shortly after at t=15.0 (delta_t = 5.0 > 0 => Long-Term Potentiation)
    new_weight = synapse.on_post_spike(t_post=15.0)

    assert new_weight > 0.5, "LTP must increase synaptic weight when post spike follows pre spike"

def test_spiking_memory_network_simulation():
    net = SpikingMemoryNetwork()
    net.connect_synapse("note_1", "note_2", weight=0.6)

    # Simulate 15 time steps with strong input to note_1
    spikes_recorded = []
    for step in range(15):
        spikes = net.simulate_step(current_inputs={"note_1": 40.0}, dt=1.0, time_step=float(step))
        spikes_recorded.append(spikes)

    # Confirm network step simulation ran cleanly
    assert len(spikes_recorded) == 15
    assert "note_1" in net.neurons
    assert "note_2" in net.neurons
